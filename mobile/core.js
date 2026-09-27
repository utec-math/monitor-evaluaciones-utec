export const MAX_CLIP_BYTES = 20 * 1024 * 1024;
export const MAX_CLIP_MS = 15 * 60 * 1000;
export const MAX_EVENTS = 500;
export const STALE_MS = 20000;

export function validKey(value) {
  return typeof value === 'string' && /^[A-Za-z0-9_-]{1,160}$/.test(value);
}
export function mobileState(status, now, live = false) {
  if (!status) return { level: 'pending', text: 'Esperando al celular' };
  if (!status.connected || now - status.lastSeen > STALE_MS)
    return { level: 'warning', text: 'Celular sin señal reciente' };
  if (!status.visible) return { level: 'warning', text: 'Página del celular oculta' };
  if (!status.camera) return { level: 'warning', text: 'Cámara interrumpida' };
  if (!live) return { level: 'pending', text: 'Celular conectado · esperando video' };
  return { level: 'ok', text: 'Cámara en directo · sin grabación' };
}
export function recordingMime(Recorder) {
  return ['video/webm;codecs=vp8', 'video/webm', 'video/mp4'].find(type => Recorder?.isTypeSupported(type)) || '';
}
export function clipFileName(session, studentUid, startedAt, mime) {
  return `celular_${session}_${studentUid}_${new Date(startedAt).toISOString().replace(/[:.]/g, '-')}.${mime.includes('mp4') ? 'mp4' : 'webm'}`;
}

// No MediaRecorder is created before an explicit Start operation and mobile acknowledgement.
export class ManualRecorder {
  constructor({ Recorder = globalThis.MediaRecorder, onComplete, onError, onLimit = () => {} }) {
    Object.assign(this, { Recorder, onComplete, onError, onLimit });
    this.state = 'idle';
  }
  start(stream, metadata) {
    if (this.state !== 'idle') throw new Error('Ya hay una grabación en curso');
    if (!stream?.getVideoTracks().some(t => t.readyState === 'live' && !t.muted)) throw new Error('No hay video disponible');
    const mimeType = recordingMime(this.Recorder);
    if (!mimeType) throw new Error('Este navegador no puede grabar. Usá Chrome o Edge actualizado.');
    const recorder = new this.Recorder(stream, { mimeType, videoBitsPerSecond: 600000 });
    this.recorder = recorder;
    this.state = 'recording';
    this.metadata = { ...metadata, mimeType };
    this.parts = [];
    this.bytes = 0;
    this.done = new Promise(resolve => { this.resolve = resolve; });
    recorder.ondataavailable = e => {
      if (!e.data?.size) return;
      this.parts.push(e.data);
      this.bytes += e.data.size;
      if (this.bytes >= MAX_CLIP_BYTES && this.state === 'recording') {
        this.onLimit('Se alcanzó el tamaño máximo del clip.');
        this.stop('size_limit');
      }
    };
    recorder.onerror = e => { this.onError(e.error || new Error('Error de grabación')); this.stop('recorder_error'); };
    recorder.onstop = () => {
      clearTimeout(this.timer);
      const blob = new Blob(this.parts, { type: recorder.mimeType || mimeType });
      this.parts = [];
      this.state = 'idle';
      const result = { blob, ...this.metadata, endedAt: Date.now(), reason: this.reason || 'manual' };
      this.onComplete(result);
      this.resolve(result);
    };
    try { recorder.start(1000); } catch (error) { this.state = 'idle'; this.parts = []; throw error; }
    this.timer = setTimeout(() => { this.onLimit('Se alcanzaron 15 minutos por clip.'); this.stop('duration_limit'); }, MAX_CLIP_MS);
  }
  stop(reason = 'manual') {
    if (this.state !== 'recording') return this.done || Promise.resolve(null);
    this.reason = reason;
    this.state = 'stopping';
    clearTimeout(this.timer);
    if (this.recorder.state !== 'inactive') this.recorder.stop();
    return this.done;
  }
}
