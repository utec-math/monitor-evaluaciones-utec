// Public settings only. TURN secrets live in Cloud Functions Secret Manager.
export const mobileConfig = Object.freeze({ region: 'us-central1', heartbeatMs: 5000, staleMs: 20000 });
