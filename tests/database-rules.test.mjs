import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import {
  assertFails,
  assertSucceeds,
  initializeTestEnvironment
} from '@firebase/rules-unit-testing';
import { get, push, ref, set, update } from 'firebase/database';

const projectId = 'demo-preciencia1';
const rules = await readFile(new URL('../database.rules.json', import.meta.url), 'utf8');
const databaseHost = process.env.FIREBASE_DATABASE_EMULATOR_HOST || '127.0.0.1:9000';
const [host, portText] = databaseHost.split(':');
const databaseUrl = `http://${databaseHost}?ns=${projectId}-default-rtdb`;
const env = await initializeTestEnvironment({ projectId, database: { host, port: Number(portText), rules } });
const database = context => context.database(databaseUrl);

const session = 'EVAL-SECURE1';
const adminUid = 'admin-uid';
const studentUid = 'student-uid';
const otherUid = 'other-uid';

try {
  await env.withSecurityRulesDisabled(async context => {
    const db = database(context);
    await set(ref(db, `admins/${adminUid}`), true);
    await set(ref(db, `sessions/${session}/config`), {
      homeUrl: 'https://example.edu/evaluacion',
      allowedSites: [],
      active: true,
      createdAt: Date.now(),
      updatedAt: Date.now()
    });
  });

  const anonymousDb = database(env.unauthenticatedContext());
  const fakeTeacherDb = database(env.authenticatedContext('email-user', { email: 'someone@example.com' }));
  const adminDb = database(env.authenticatedContext(adminUid, { email: 'teacher@example.edu' }));
  const studentDb = database(env.authenticatedContext(studentUid));
  const otherDb = database(env.authenticatedContext(otherUid));

  await assertFails(get(ref(anonymousDb, `sessions/${session}/config`)));
  await assertFails(get(ref(fakeTeacherDb, `sessions/${session}`)));
  await assertSucceeds(get(ref(adminDb, `sessions/${session}`)));

  const client = {
    id: '12345678',
    uid: studentUid,
    name: 'Estudiante de prueba',
    lastSeen: Date.now(),
    connected: true,
    app: 'windows-webview2',
    version: '0.9',
    state: 'locked',
    currentUrl: 'https://example.edu/evaluacion',
    eventCapture: true
  };

  await assertFails(set(ref(anonymousDb, `sessions/${session}/clients/${studentUid}`), client));
  await assertFails(set(ref(otherDb, `sessions/${session}/clients/${studentUid}`), { ...client, uid: otherUid }));
  await assertSucceeds(set(ref(studentDb, `sessions/${session}/clients/${studentUid}`), client));
  await assertSucceeds(get(ref(studentDb, `sessions/${session}/config`)));
  await assertFails(get(ref(otherDb, `sessions/${session}/clients/${studentUid}`)));
  await assertFails(get(ref(studentDb, `sessions/${session}`)));

  await assertFails(update(ref(studentDb, `sessions/${session}/clients/${studentUid}`), { id: '87654321' }));

  const validEvent = {
    ts: Date.now(),
    studentUid,
    studentId: client.id,
    studentName: client.name,
    type: 'cambio_aplicacion',
    level: 'yellow',
    detail: 'La aplicación perdió el foco.',
    clipUrl: '',
    clipFileId: ''
  };
  await assertSucceeds(push(ref(studentDb, `sessions/${session}/events`), validEvent));
  await assertFails(push(ref(studentDb, `sessions/${session}/events`), { ...validEvent, studentId: 'otro-documento' }));
  await assertFails(get(ref(studentDb, `sessions/${session}/events`)));

  const command = { id: 'cmd-1', action: 'reload', issuedAt: Date.now(), expiresAt: Date.now() + 30000, durationSec: 0 };
  await assertSucceeds(set(ref(adminDb, `sessions/${session}/commands/${studentUid}`), command));
  await assertSucceeds(get(ref(studentDb, `sessions/${session}/commands/${studentUid}`)));
  await assertFails(get(ref(otherDb, `sessions/${session}/commands/${studentUid}`)));

  await assertSucceeds(update(ref(adminDb, `sessions/${session}/config`), { active: false, updatedAt: Date.now() }));
  await assertFails(update(ref(studentDb, `sessions/${session}/clients/${studentUid}`), { lastSeen: Date.now() }));
  await assertFails(push(ref(studentDb, `sessions/${session}/events`), validEvent));

  console.log('Todas las pruebas de seguridad de Firebase pasaron.');
} finally {
  await env.cleanup();
}
