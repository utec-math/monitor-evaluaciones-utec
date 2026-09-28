import { initializeApp, getAuth, signInAnonymously, setPersistence, browserSessionPersistence, getDatabase, ref, get, set, onValue, serverTimestamp } from './firebase.js';
import { firebaseConfig } from '../firebase-config.js';
import { mobileConfig } from '../mobile-config.js';
import { validKey, MAX_EVENTS } from './core.js';
import { VideoPeer } from './peer.js';

const $ = id => document.getElementById(id);
const params = new URLSearchParams(location.hash.slice(1));
const session = params.get('session'), token = params.get('token');
const app = initializeApp(firebaseConfig, 'mobile-phone');
const auth = getAuth(app), db = getDatabase(app);
let stream, peer, timer, wakeLock, started = false, starting = false, firebaseOnline = false, offset = 0;
let recordingAck = '', recordingCommand = null, viewer = null, generation = 0, cameraActive = false;
let cleanups = [], eventSequence = 0, eventLast = '', eventLastAt = 0;
const base = `mobileSessions/${session}`, statusPath = `${base}/status/${token}`, rtcPath = `${base}/rtc/${token}`;
const now = () => Date.now() + offset;
const say = text => { $('mobileStatus').textContent = text; };
async function claimPair() {
  const pairPath = `${base}/pairs/${token}`;
  // The unguessable QR token is the invitation. Rules permit only the first
  // anonymous account to write its own UID, or that same account to resume.
  await set(ref(db, `${pairPath}/claimedBy`), auth.currentUser.uid);
  const pair = (await get(ref(db, pairPath))).val();
  if (!pair || pair.claimedBy !== auth.currentUser.uid) throw new Error('El QR ya no está disponible.');
  await set(ref(db, `mobileMembers/${session}/${auth.currentUser.uid}`), { token, studentUid: pair.studentUid });
  const name = (await get(ref(db, `sessions/${session}/clients/${pair.studentUid}/name`))).val();
  return { studentUid: pair.studentUid, name: name || 'Estudiante', expiresAt: pair.expiresAt };
}

async function event(type) {
  if (!started || !firebaseOnline || eventSequence >= MAX_EVENTS) return;
  if (eventLast === type && Date.now() - eventLastAt < 2000) return;
  eventLast = type; eventLastAt = Date.now();
  const id = String(++eventSequence);
  try { await set(ref(db, `${base}/events/${token}/${id}`), { type, ts: serverTimestamp(), source: 'mobile' }); } catch { /* Presence timeout remains observable. */ }
}
function showRecording() {
  const active = started && firebaseOnline && viewer?.expiresAt > now() && recordingCommand?.active &&
    recordingCommand.viewerId === viewer.id && cameraActive && document.visibilityState === 'visible';
  $('recordingNotice').hidden = !active;
  recordingAck = active ? recordingCommand.id : '';
}
async function presence() {
  if (!started || !firebaseOnline) return;
  showRecording();
  try {
    await set(ref(db, statusPath), { connected: true, lastSeen: serverTimestamp(), visible: document.visibilityState === 'visible',
      focused: document.hasFocus(), camera: cameraActive, recordingAck });
  } catch { await stop('La sesión terminó o el vínculo fue revocado.'); }
}
async function keepAwake() {
  try { if (started && document.visibilityState === 'visible') wakeLock = await navigator.wakeLock?.request('screen'); } catch { }
}
async function stop(message = 'Cámara detenida. Podés volver a activarla si la evaluación sigue abierta.') {
  started = false; generation++; clearInterval(timer); peer?.close(); peer = null;
  stream?.getTracks().forEach(track => track.stop()); stream = null; cameraActive = false;
  cleanups.splice(0).forEach(fn => fn());
  wakeLock?.release().catch(() => {});
  $('preview').srcObject = null; $('recordingNotice').hidden = true;
  $('mobileConsent').hidden = false; $('stopCamera').hidden = true;
  $('startCamera').disabled = !$('consent').checked;
  say(message);
  if (firebaseOnline) set(ref(db, statusPath), {
    connected: false, lastSeen: serverTimestamp(), visible: document.visibilityState === 'visible',
    focused: document.hasFocus(), camera: false, recordingAck: ''
  }).catch(() => {});
}

async function acceptOffer(offer) {
  if (!offer?.id || !started || offer.id === peer?.id) return;
  const attempt = ++generation;
  peer?.close(); peer = null;
  try {
    const { iceServers } = mobileConfig;
    if (!started || attempt !== generation) return;
    peer = new VideoPeer({ db, path: rtcPath, id: offer.id, iceServers, role: 'mobile', stream,
      onState: state => { if (started) say(state === 'connected' ? 'Cámara compartida con el docente.' : 'Conectando video con el docente…'); },
      onError: () => { if (started) say('No se pudo conectar el video. El docente puede reintentar la conexión.'); } });
    await peer.answer(offer);
  } catch (error) { say(error.message || 'No se pudo conectar el video.'); }
}

async function start() {
  if (started || starting || !$('consent').checked) return;
  starting = true; $('startCamera').disabled = true;
  try {
    if (!isSecureContext || !navigator.mediaDevices?.getUserMedia) throw new Error('Abrí esta página con HTTPS en un navegador con cámara.');
    const identity = await claimPair();
    $('mobileIdentity').textContent = `${identity.name} · Sesión ${session}`;
    // Resume the bounded event counter after a reload; no images are retained.
    const existing = (await get(ref(db, `${base}/events/${token}`))).val() || {};
    eventSequence = Math.max(0, ...Object.keys(existing).filter(k => /^\d+$/.test(k)).map(Number));
    stream = await navigator.mediaDevices.getUserMedia({ audio: false, video: { facingMode: { ideal: $('facing').value }, width: { ideal: 640 }, height: { ideal: 480 }, frameRate: { ideal: 12, max: 15 } } });
    started = true; cameraActive = true;
    $('preview').srcObject = stream; await $('preview').play();
    $('mobileConsent').hidden = true; $('stopCamera').hidden = false;
    const track = stream.getVideoTracks()[0];
    track.onended = () => { cameraActive = false; event('camera_lost'); stop('La cámara se interrumpió. Volvé a activarla.'); };
    track.onmute = () => { cameraActive = false; event('camera_lost'); presence(); };
    track.onunmute = () => { cameraActive = true; event('camera_restored'); presence(); };
    cleanups.push(onValue(ref(db, `${base}/pairs/${token}`), snap => {
      const p = snap.val(); if (!p || p.revoked || p.expiresAt <= now()) stop('El vínculo de cámara finalizó.');
    }, () => stop('El vínculo de cámara ya no está disponible.')));
    cleanups.push(onValue(ref(db, `sessions/${session}/config/active`), snap => { if (snap.val() !== true) stop('La evaluación finalizó.'); }, () => stop('La evaluación finalizó.')));
    cleanups.push(onValue(ref(db, `sessions/${session}/clients/${identity.studentUid}/state`), snap => { if (!snap.exists() || snap.val() === 'finished') stop('La evaluación finalizó para este estudiante.'); }, () => stop('La evaluación finalizó.')));
    cleanups.push(onValue(ref(db, `${rtcPath}/offer`), snap => acceptOffer(snap.val()), () => stop('Se cerró el acceso al video.')));
    cleanups.push(onValue(ref(db, `${rtcPath}/viewer`), snap => { viewer = snap.val(); showRecording(); }, () => stop('Se cerró el acceso al video.')));
    cleanups.push(onValue(ref(db, `${rtcPath}/recording`), snap => { recordingCommand = snap.val(); showRecording(); presence(); }, () => stop('Se cerró el acceso al video.')));
    cleanups.push(onValue(ref(db, '.info/connected'), async snap => {
      firebaseOnline = snap.val() === true;
      showRecording();
      if (firebaseOnline && started) {
        // A missed heartbeat is visible to the teacher after 20 seconds.
        // Do not block the camera on a separate disconnect registration.
        await presence();
        if (started) await event('connected');
      } else if (started) say('Sin conexión con el monitor. Intentando reconectar…');
    }));
    timer = setInterval(() => { if (now() >= identity.expiresAt) stop('El vínculo venció.'); else presence(); }, mobileConfig.heartbeatMs);
    await keepAwake(); say('Cámara activa. Esperando que el docente abra el video.');
  } catch (error) { await stop(error.name === 'NotAllowedError' ? 'Permití el acceso a la cámara para continuar.' : error.message || 'No se pudo activar la cámara.'); }
  finally { starting = false; if (!started) $('startCamera').disabled = !$('consent').checked; }
}

document.addEventListener('visibilitychange', () => { if (!started) return; event(document.hidden ? 'hidden' : 'visible'); presence(); if (!document.hidden) keepAwake(); });
window.addEventListener('blur', () => { event('blur'); presence(); });
window.addEventListener('focus', () => { event('focus'); presence(); });
window.addEventListener('pagehide', () => { event('pagehide'); stop('Cámara detenida al salir de la página.'); });
$('startCamera').onclick = start;
$('stopCamera').onclick = () => stop();
$('consent').onchange = () => { $('startCamera').disabled = !$('consent').checked || starting; };
try {
  if (!validKey(session) || !validKey(token)) throw new Error('Este enlace no es válido. Escaneá el QR individual de la evaluación.');
  await setPersistence(auth, browserSessionPersistence);
  await signInAnonymously(auth);
  onValue(ref(db, '.info/serverTimeOffset'), snap => { offset = Number(snap.val()) || 0; });
  $('mobileIdentity').textContent = `Sesión ${session}`;
  say('Leé la información y activá la cámara para vincular el teléfono.');
} catch (error) { say(error.message || 'No se pudo preparar la conexión.'); $('mobileConsent').hidden = true; }

