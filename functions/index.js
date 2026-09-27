import { randomBytes } from 'node:crypto';
import { initializeApp } from 'firebase-admin/app';
import { getAuth } from 'firebase-admin/auth';
import { getDatabase } from 'firebase-admin/database';
import { getStorage } from 'firebase-admin/storage';
import { onCall, onRequest, HttpsError } from 'firebase-functions/v2/https';
import { onSchedule } from 'firebase-functions/v2/scheduler';
import { defineSecret, defineString } from 'firebase-functions/params';
import { key, canClaim, turnCredentials, INVITE_MS, RETENTION_MS } from './domain.js';

initializeApp();
const db = getDatabase();
const TURN_SECRET = defineSecret('MOBILE_TURN_SECRET');
const TURN_URLS = defineString('MOBILE_TURN_URLS', { default: '' });
const CLIP_BUCKET = defineString('MOBILE_CLIP_BUCKET');
const OPTIONS = { region: 'us-central1', maxInstances: 5 };
const allowedOrigins = ['https://utec-math.github.io'];
const read = async path => (await db.ref(path).get()).val();
const randomId = () => randomBytes(24).toString('hex');
function uid(request) {
  if (!request.auth) throw new HttpsError('unauthenticated', 'Ingresá a la sesión.');
  return request.auth.uid;
}
async function teacher(id) {
  if (await read(`admins/${id}`) !== true) throw new HttpsError('permission-denied', 'Acceso docente requerido.');
}
async function active(session, studentUid) {
  if (await read(`sessions/${session}/config/active`) !== true) throw new HttpsError('failed-precondition', 'La sesión está cerrada.');
  const client = await read(`sessions/${session}/clients/${studentUid}`);
  if (!client || client.state === 'finished') throw new HttpsError('failed-precondition', 'El estudiante no está activo.');
  return client;
}

export const createMobilePair = onCall(OPTIONS, async request => {
  await teacher(uid(request));
  const session = key(request.data.session), studentUid = key(request.data.studentUid);
  await active(session, studentUid);
  const token = randomId(), now = Date.now();
  const base = `mobileSessions/${session}`;
  // Transaction makes replacement atomic: even simultaneous clicks leave one active invitation.
  await db.ref(base).transaction(current => {
    const value = current || {};
    value.pairs ||= {}; value.links ||= {};
    const previous = value.links[studentUid]?.token;
    if (previous && value.pairs[previous]) value.pairs[previous].revoked = true;
    value.pairs[token] = { studentUid, claimedBy: '', revoked: false, createdAt: now, inviteExpiresAt: now + INVITE_MS, expiresAt: now + RETENTION_MS };
    value.links[studentUid] = { token, inviteExpiresAt: now + INVITE_MS };
    return value;
  });
  return { token, inviteExpiresAt: now + INVITE_MS };
});

export const claimMobilePair = onCall(OPTIONS, async request => {
  const mobileUid = uid(request), session = key(request.data.session), token = key(request.data.token);
  // A separate auth app on the phone must use anonymous auth, never a teacher's browser identity.
  if (request.auth.token.firebase?.sign_in_provider !== 'anonymous') throw new HttpsError('permission-denied', 'Usá la página móvil.');
  const pairRef = db.ref(`mobileSessions/${session}/pairs/${token}`);
  const before = (await pairRef.get()).val();
  if (!before) throw new HttpsError('not-found', 'QR no válido.');
  await active(session, before.studentUid);
  const result = await pairRef.transaction(pair => canClaim(pair, mobileUid, Date.now()) ? { ...pair, claimedBy: mobileUid } : undefined);
  if (!result.committed) throw new HttpsError('permission-denied', 'QR vencido, revocado o ya utilizado. Pedí uno nuevo.');
  const pair = result.snapshot.val();
  await db.ref(`mobileMembers/${session}/${mobileUid}`).set({ token, studentUid: pair.studentUid });
  const client = await active(session, pair.studentUid);
  return { studentUid: pair.studentUid, name: client.name, expiresAt: pair.expiresAt };
});

export const mobileIceServers = onCall({ ...OPTIONS, secrets: [TURN_SECRET] }, async request => {
  const id = uid(request), session = key(request.data.session), token = key(request.data.token);
  const pair = await read(`mobileSessions/${session}/pairs/${token}`);
  if (!pair || pair.revoked || pair.expiresAt <= Date.now()) throw new HttpsError('permission-denied', 'Vínculo vencido.');
  if (pair.claimedBy !== id) await teacher(id);
  await active(session, pair.studentUid);
  const urls = TURN_URLS.value().split(',').map(x => x.trim()).filter(Boolean);
  if (!urls.length || !urls.every(x => /^turns?:/.test(x))) throw new HttpsError('failed-precondition', 'Falta configurar el servicio de video TURN.');
  return { iceServers: [turnCredentials(id, TURN_SECRET.value(), urls)] };
});

// Videos stay private in Cloud Storage. A verified admin may request a short-lived viewing URL.
export const uploadMobileClip = onRequest({ ...OPTIONS, cors: allowedOrigins, memory: '256MiB', timeoutSeconds: 120 }, async (req, res) => {
  try {
    if (req.method !== 'POST') { res.status(405).send('Solo POST'); return; }
    const bearer = /^Bearer (.+)$/.exec(req.headers.authorization || '');
    if (!bearer) { res.status(401).send('Falta autenticación'); return; }
    const identity = await getAuth().verifyIdToken(bearer[1], true);
    await teacher(identity.uid);
    const session = key(req.query.session), token = key(req.query.token), clipId = key(req.query.clipId);
    const pair = await read(`mobileSessions/${session}/pairs/${token}`);
    const intent = await read(`mobileSessions/${session}/recordings/${token}/${clipId}`);
    if (!pair || !intent || intent.teacherUid !== identity.uid || !intent.acknowledgedAt ||
        pair.expiresAt <= Date.now() || intent.startedAt > Date.now() || Date.now() - intent.startedAt > 60 * 60 * 1000)
      throw new HttpsError('permission-denied', 'No hay una grabación manual autorizada.');
    const contentType = String(req.headers['content-type'] || '').split(';')[0];
    if (!['video/webm', 'video/mp4'].includes(contentType) || !req.rawBody?.length || req.rawBody.length > 25 * 1024 * 1024) {
      res.status(400).send('Formato o tamaño no permitido (máximo 25 MB).'); return;
    }
    const path = `mobile-clips/${session}/${token}/${clipId}.${contentType === 'video/mp4' ? 'mp4' : 'webm'}`;
    const file = getStorage().bucket(CLIP_BUCKET.value()).file(path);
    // Deterministic ID makes retries safe. Never publish a public ACL or a persistent download token.
    await file.save(req.rawBody, { resumable: false, contentType, metadata: { cacheControl: 'private, no-store' } });
    if (pair.expiresAt <= Date.now() || !await read(`mobileSessions/${session}/pairs/${token}`)) {
      await file.delete({ ignoreNotFound: true });
      throw new HttpsError('permission-denied', 'El vínculo venció durante la subida.');
    }
    const metadata = { path, studentUid: pair.studentUid, startedAt: intent.startedAt, endedAt: Number(req.query.endedAt),
      reason: String(req.query.reason || 'manual').slice(0, 80), expiresAt: pair.expiresAt, bytes: req.rawBody.length, teacherUid: identity.uid };
    if (!Number.isFinite(metadata.endedAt) || metadata.endedAt < intent.startedAt || metadata.endedAt > Date.now() + 60000) {
      await file.delete({ ignoreNotFound: true }); res.status(400).send('Hora de fin inválida'); return;
    }
    await db.ref(`mobileSessions/${session}/clips/${clipId}`).set(metadata);
    res.json({ ok: true, clipId });
  } catch (error) { res.status(error.code === 'permission-denied' ? 403 : 400).json({ error: error.message || 'No se pudo guardar el clip.' }); }
});

export const getMobileClipUrl = onCall(OPTIONS, async request => {
  await teacher(uid(request));
  const clip = await read(`mobileSessions/${key(request.data.session)}/clips/${key(request.data.clipId)}`);
  if (!clip || clip.expiresAt <= Date.now()) throw new HttpsError('not-found', 'Clip vencido o inexistente.');
  const [url] = await getStorage().bucket(CLIP_BUCKET.value()).file(clip.path).getSignedUrl({ action: 'read', expires: Math.min(Date.now() + 5 * 60 * 1000, clip.expiresAt) });
  return { url };
});

export const cleanupMobileData = onSchedule({ ...OPTIONS, schedule: 'every 60 minutes', timeZone: 'Etc/UTC', timeoutSeconds: 540 }, async () => {
  const sessions = (await db.ref('mobileSessions').get()).val() || {};
  for (const [session, value] of Object.entries(sessions)) {
    for (const [token, pair] of Object.entries(value.pairs || {})) {
      if (pair.expiresAt > Date.now()) continue;
      await getStorage().bucket(CLIP_BUCKET.value()).deleteFiles({ prefix: `mobile-clips/${session}/${token}/` });
      // Transaction avoids removing a newer link created while cleanup was deleting old media.
      await db.ref(`mobileSessions/${session}`).transaction(current => {
        if (!current?.pairs?.[token] || current.pairs[token].expiresAt > Date.now()) return;
        delete current.pairs[token];
        for (const child of ['status', 'rtc', 'events', 'recordings']) if (current[child]) delete current[child][token];
        if (current.links?.[pair.studentUid]?.token === token) delete current.links[pair.studentUid];
        for (const [id, clip] of Object.entries(current.clips || {})) if (clip.expiresAt <= Date.now()) delete current.clips[id];
        return current;
      });
      if (pair.claimedBy) await db.ref(`mobileMembers/${session}/${pair.claimedBy}`).transaction(member => member?.token === token ? null : undefined);
    }
  }
});
