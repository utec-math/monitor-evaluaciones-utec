import { getFunctions, httpsCallable, ref, get, set, update, onValue, onDisconnect, serverTimestamp, runTransaction } from './firebase.js';
import { mobileConfig } from '../mobile-config.js';
import { ManualRecorder, mobileState, clipFileName, STALE_MS } from './core.js';
import { VideoPeer } from './peer.js';

const EVENT_NAMES = { connected: 'Conectado', hidden: 'Página oculta', visible: 'Página visible', blur: 'Pérdida de foco', focus: 'Regreso del foco', pagehide: 'Salida de la página', camera_lost: 'Cámara interrumpida', camera_restored: 'Cámara recuperada', connection_lost: 'Sin señal reciente', recording_started: 'Grabación iniciada', recording_stopped: 'Grabación detenida' };
const stamp = ts => ts ? new Date(ts).toLocaleTimeString() : '—';
const element = (tag, className, text) => { const e = document.createElement(tag); e.className = className; if (text) e.textContent = text; return e; };
const button = (text, action) => { const b = element('button', '', text); b.type = 'button'; b.onclick = action; return b; };
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

export function createMobilePanel({ app, db, auth, container, beep, soundEnabled }) {
  const functions = getFunctions(app, mobileConfig.region);
  let session = '', active = false, clients = {}, data = {}, unsubscribe, offset = 0, dbOnline = false;
  let epoch = 0, disposed = false, warned = '';
  const cards = new Map();
  const now = () => Date.now() + offset;
  const call = (name, value) => httpsCallable(functions, name)(value).then(r => r.data);
  const report = text => { let e = container.querySelector('.mobile-global-message'); if (!e) { e = element('p', 'mobile-global-message'); container.prepend(e); } e.textContent = text; };
  onValue(ref(db, '.info/serverTimeOffset'), snap => { offset = Number(snap.val()) || 0; });
  onValue(ref(db, '.info/connected'), snap => { dbOnline = snap.val() === true; if (!dbOnline) cards.forEach(c => c.stop('teacher_disconnected')); });

  class CameraCard {
    constructor(studentUid) {
      this.studentUid = studentUid; this.session = session; this.token = ''; this.id = ''; this.generation = 0;
      this.pendingClips = new Map(); this.urls = new Set(); this.warning = ''; this.live = false;
      this.root = element('section', 'mobile-panel');
      this.name = element('h3', '', 'Cámara móvil');
      this.state = element('p', 'mobile-state');
      this.video = element('video'); Object.assign(this.video, { autoplay: true, muted: true, playsInline: true });
      this.video.setAttribute('aria-label', 'Cámara del celular en directo');
      this.recording = element('p', 'mobile-recording', '● Grabando cámara móvil'); this.recording.hidden = true;
      this.actions = element('div', 'mobile-actions');
      this.pairButton = button('Generar QR', () => this.pair());
      this.viewButton = button('Ver cámara', () => this.view());
      this.startButton = button('Grabar', () => this.start());
      this.stopButton = button('Detener grabación', () => this.stop('manual'));
      this.closeButton = button('Cerrar video', () => this.disconnect('viewer_closed'));
      this.stopButton.className = 'mobile-danger';
      this.actions.append(this.pairButton, this.viewButton, this.startButton, this.stopButton, this.closeButton);
      this.qr = element('div', 'mobile-qr');
      this.message = element('p', 'mobile-message');
      this.events = element('div', 'mobile-events');
      this.clips = element('div');
      this.root.append(this.name, this.state, this.video, this.recording, this.actions, this.qr, this.message, this.events, this.clips);
      container.append(this.root);
      this.recorder = new ManualRecorder({ onComplete: clip => this.complete(clip), onError: error => this.say(error.message), onLimit: text => this.say(text) });
    }
    get path() { return `mobileSessions/${this.session}`; }
    get rtc() { return `${this.path}/rtc/${this.token}`; }
    get busy() { return this.starting || this.recorder.state !== 'idle' || this.pendingClips.size > 0; }
    say(text) { this.message.textContent = text; }
    async log(type) {
      if (!this.token || !dbOnline) return;
      const id = `${type}_${this.id || 'panel'}_${Math.floor(now() / 2000)}`.slice(0, 80);
      try { await set(ref(db, `${this.path}/events/${this.token}/${id}`), { type, ts: serverTimestamp(), source: 'teacher' }); } catch { }
    }
    refresh() {
      const client = clients[this.studentUid];
      this.name.textContent = `Cámara móvil · ${client?.name || this.studentUid}`;
      const token = data.links?.[this.studentUid]?.token || '';
      if (token !== this.token) { this.disconnect('pair_replaced'); this.token = token; this.qr.replaceChildren(); }
      const pair = data.pairs?.[this.token], status = data.status?.[this.token];
      this.status = status;
      const ended = !active || !client || client.state === 'finished' || pair?.revoked || (pair && pair.expiresAt <= now());
      if (ended) this.disconnect('evaluation_finished');
      const state = ended ? { level: 'pending', text: 'Cámara finalizada' } : mobileState(status, now(), this.live);
      if (!dbOnline) { state.level = 'warning'; state.text = 'Tu panel está sin conexión'; }
      this.state.textContent = this.recorder.state !== 'idle' ? '● Grabando cámara móvil' : state.text;
      this.state.dataset.level = state.level;
      this.recording.hidden = this.recorder.state === 'idle';
      const lease = data.rtc?.[this.token]?.viewer;
      if (this.peer && (lease?.id !== this.id || lease.expiresAt <= now())) this.disconnect('viewer_lease_lost');
      if (this.recorder.state === 'recording' && (state.level !== 'ok' || status?.recordingAck !== this.clipId)) this.stop('supervision_interrupted');
      if (this.warning !== state.text && state.level === 'warning' && status && dbOnline) {
        if (soundEnabled()) beep();
        if (state.text.includes('sin señal')) this.log('connection_lost');
      }
      this.warning = state.text;
      this.pairButton.disabled = ended || this.busy || !!this.pairing;
      this.viewButton.disabled = ended || !pair?.claimedBy || !!this.peer || !!this.connecting || !dbOnline;
      this.startButton.disabled = ended || state.level !== 'ok' || this.busy || !dbOnline;
      this.stopButton.disabled = this.recorder.state !== 'recording' && !this.starting;
      this.closeButton.disabled = !this.peer && !this.connecting;
      const eventList = Object.values(data.events?.[this.token] || {}).sort((a, b) => b.ts - a.ts).slice(0, 8);
      this.events.replaceChildren(...eventList.map(e => element('p', '', `${stamp(e.ts)} · ${EVENT_NAMES[e.type] || e.type}`)));
      this.renderSaved();
    }
    async pair() {
      if (this.busy || this.pairing) return;
      this.pairing = true; this.refresh();
      try {
        const result = await call('createMobilePair', { session: this.session, studentUid: this.studentUid });
        if (this.session !== session) return;
        const url = new URL('celular.html', location.href); url.hash = new URLSearchParams({ session: this.session, token: result.token });
        this.qr.replaceChildren();
        new window.QRCode(this.qr, { text: url.href, width: 220, height: 220, correctLevel: window.QRCode.CorrectLevel.M });
        const copy = button('Copiar enlace individual', async () => { try { await navigator.clipboard.writeText(url.href); this.say('Enlace copiado. Compartilo solo con este estudiante.'); } catch { this.say(url.href); } });
        this.qr.append(copy);
        this.say('QR válido por 5 minutos y para un solo teléfono. También aparece al pulsar «Vincular celular» en la app Windows actualizada.');
      } catch (error) { this.say(error.message || 'No se pudo crear el QR. Verificá el despliegue de la función móvil.'); }
      finally { this.pairing = false; this.refresh(); }
    }
    async view() {
      if (this.peer || this.connecting || !this.token) return;
      this.connecting = true; const generation = ++this.generation; this.id = crypto.randomUUID();
      const id = this.id, rtc = this.rtc;
      try {
        const result = await runTransaction(ref(db, `${rtc}/viewer`), current => {
          if (current && current.expiresAt > now() && current.id !== id) return;
          return { id, uid: auth.currentUser.uid, expiresAt: now() + 25000 };
        }, { applyLocally: false });
        if (!result.committed) throw new Error('Esta cámara está abierta en otro panel. Cerralo o esperá 25 segundos.');
        const { iceServers } = await call('mobileIceServers', { session: this.session, token: this.token });
        if (generation !== this.generation || this.session !== session) return;
        const off = { id: '', active: false, startedAt: 0, viewerId: id };
        await onDisconnect(ref(db, `${rtc}/recording`)).set(off);
        await set(ref(db, `${rtc}/recording`), off);
        this.peer = new VideoPeer({ db, path: rtc, id, iceServers, role: 'teacher',
          onStream: stream => { this.video.srcObject = stream; this.stream = stream; this.video.play().catch(() => this.say('Pulsá el video para reproducirlo.')); },
          onState: state => {
            this.live = state === 'connected';
            if (['disconnected', 'failed', 'closed'].includes(state)) { this.stop('video_interrupted'); this.say('Video interrumpido. Cerrá el video y pulsá «Ver cámara» para reconectar.'); }
            this.refresh();
          }, onError: error => this.say(`Conexión de video: ${error.message || 'no disponible'}`) });
        this.video.onclick = () => this.video.play().catch(() => {});
        await this.peer.offer();
        this.leaseTimer = setInterval(async () => {
          try { await set(ref(db, `${rtc}/viewer`), { id, uid: auth.currentUser.uid, expiresAt: now() + 25000 }); }
          catch { this.disconnect('lease_error'); }
        }, 5000);
        this.say('Conectando cámara. La transmisión no se graba hasta pulsar «Grabar».');
      } catch (error) { this.say(error.message || 'No se pudo abrir el video.'); this.disconnect('connect_error'); }
      finally { this.connecting = false; this.refresh(); }
    }
    async start() {
      if (this.busy || !this.live || !dbOnline) return;
      this.starting = true; this.cancelStart = false;
      const token = this.token, id = crypto.randomUUID(), rtc = this.rtc, viewerId = this.id;
      this.clipId = id;
      try {
        await set(ref(db, `${rtc}/recording`), { id, active: true, startedAt: now(), viewerId });
        // The phone displays the indicator before acknowledging. Do not record without it.
        const deadline = Date.now() + 6000;
        while (this.status?.recordingAck !== id && Date.now() < deadline && !this.cancelStart && dbOnline && this.live) await delay(100);
        if (this.cancelStart || !dbOnline || !this.live || this.status?.recordingAck !== id || token !== this.token) throw new Error('El celular no confirmó el aviso. No se inició la grabación.');
        const startedAt = now();
        await set(ref(db, `${this.path}/recordings/${token}/${id}`), { teacherUid: auth.currentUser.uid, startedAt, acknowledgedAt: serverTimestamp() });
        if (this.cancelStart || !this.live) throw new Error('Grabación cancelada antes de comenzar.');
        this.recorder.start(this.stream, { id, token, session: this.session, studentUid: this.studentUid, startedAt });
        await this.log('recording_started');
        this.say('Grabando desde ahora. Pulsá «Detener grabación» para guardar. Límite por clip: 15 min o aproximadamente 20 MB.');
      } catch (error) {
        this.say(error.message);
        set(ref(db, `${rtc}/recording`), { id, active: false, startedAt: 0, viewerId }).catch(() => {});
      } finally { this.starting = false; this.refresh(); }
    }
    stop(reason) {
      this.cancelStart = true;
      if (this.recorder.state === 'idle') return;
      const rtc = this.rtc, viewerId = this.id, id = this.clipId;
      this.recorder.stop(reason);
      set(ref(db, `${rtc}/recording`), { id, active: false, startedAt: 0, viewerId }).catch(() => {});
      this.log('recording_stopped');
      this.recording.hidden = true;
    }
    disconnect(reason) {
      this.cancelStart = true; this.generation++;
      this.stop(reason); clearInterval(this.leaseTimer);
      this.peer?.close(); this.peer = null; this.live = false; this.stream = null; this.video.srcObject = null;
    }
    complete(clip) {
      // Server timestamps were used for Start. Correct the local End with the current offset.
      clip.endedAt += offset;
      if (!clip.blob.size) { this.say('No se recibieron imágenes para guardar.'); return; }
      const row = element('div', 'mobile-clip');
      const state = element('p', '', `Clip ${stamp(clip.startedAt)}–${stamp(clip.endedAt)} · pendiente de guardar`);
      const retry = button('Reintentar guardado', () => this.upload(clip.id));
      const download = button('Descargar copia local', () => {
        const a = document.createElement('a'), url = URL.createObjectURL(clip.blob); this.urls.add(url);
        a.href = url; a.download = clipFileName(clip.session, clip.studentUid, clip.startedAt, clip.blob.type); a.click();
        this.say('La copia descargada queda bajo tu custodia; no se borra automáticamente.');
      });
      const discard = button('Descartar copia pendiente', () => { if (confirm('¿Descartar este clip pendiente?')) { this.pendingClips.delete(clip.id); row.remove(); this.refresh(); } });
      row.append(state, retry, download, discard); this.clips.prepend(row);
      this.pendingClips.set(clip.id, { clip, row, state, retry, uploading: false });
      this.upload(clip.id); this.refresh();
    }
    async upload(id) {
      const pending = this.pendingClips.get(id); if (!pending || pending.uploading) return;
      pending.uploading = true; pending.retry.disabled = true; pending.state.textContent = 'Guardando clip privado…';
      const clip = pending.clip;
      try {
        const bearer = await auth.currentUser.getIdToken();
        const url = new URL(`https://${mobileConfig.region}-${app.options.projectId}.cloudfunctions.net/uploadMobileClip`);
        url.search = new URLSearchParams({ session: clip.session, token: clip.token, clipId: clip.id, endedAt: clip.endedAt, reason: clip.reason });
        const response = await fetch(url, { method: 'POST', headers: { Authorization: `Bearer ${bearer}`, 'Content-Type': clip.blob.type.split(';')[0] }, body: clip.blob, signal: AbortSignal.timeout(120000) });
        if (!response.ok) throw new Error('El servidor no pudo guardar el clip.');
        const result = await response.json(); if (!result.ok) throw new Error('No se confirmó el guardado.');
        this.pendingClips.delete(id); pending.row.remove(); this.say('Clip guardado con acceso docente restringido. Vence junto con el vínculo, a las 24 horas de su creación.');
      } catch (error) { pending.state.textContent = `${error.message} La copia sigue en este panel: reintentá o descargala antes de cerrar.`; }
      finally { pending.uploading = false; pending.retry.disabled = false; this.refresh(); }
    }
    renderSaved() {
      const clips = Object.entries(data.clips || {}).filter(([, clip]) => clip.studentUid === this.studentUid && clip.expiresAt > now());
      for (const [id, clip] of clips) {
        if (this.clips.querySelector(`[data-clip="${id}"]`)) continue;
        const row = element('div', 'mobile-clip'); row.dataset.clip = id;
        row.append(element('p', '', `Clip privado · ${stamp(clip.startedAt)}–${stamp(clip.endedAt)}`));
        row.append(button('Ver clip', async () => {
          try {
            const { url } = await call('getMobileClipUrl', { session: this.session, clipId: id });
            let video = row.querySelector('video'); if (!video) { video = element('video'); video.controls = true; row.append(video); }
            video.src = url; video.play().catch(() => {});
          } catch (error) { this.say(error.message || 'No se pudo abrir el clip.'); }
        }));
        this.clips.append(row);
      }
    }
    destroy() { this.disconnect('panel_closed'); this.urls.forEach(url => URL.revokeObjectURL(url)); this.root.remove(); }
  }

  function reconcile() {
    if (disposed) return;
    for (const studentUid of Object.keys(clients)) { if (!cards.has(studentUid)) cards.set(studentUid, new CameraCard(studentUid)); }
    cards.forEach(card => card.refresh());
  }
  const interval = setInterval(reconcile, 1000);
  window.addEventListener('beforeunload', e => {
    if ([...cards.values()].some(c => c.busy)) { e.preventDefault(); e.returnValue = ''; }
  });
  window.addEventListener('pagehide', () => cards.forEach(c => c.disconnect('panel_closed')));
  return {
    canLeave() {
      if (![...cards.values()].some(c => c.busy)) return true;
      report('Detené las grabaciones y guardá, descargá o descartá los clips pendientes antes de cambiar de sesión o salir.');
      return false;
    },
    setContext(next) {
      active = next.active; clients = next.clients || {};
      if (session !== next.session) {
        if (unsubscribe) unsubscribe(); epoch++;
        cards.forEach(c => c.destroy()); cards.clear(); data = {}; session = next.session; warned = '';
        if (session) {
          const current = epoch;
          unsubscribe = onValue(ref(db, `mobileSessions/${session}`), snap => { if (current === epoch) { data = snap.val() || {}; reconcile(); } }, () => {
            if (warned !== session) { warned = session; report('La cámara móvil no está habilitada todavía. La evaluación habitual continúa funcionando.'); }
          });
        }
      }
      reconcile();
    },
    dispose() { disposed = true; clearInterval(interval); unsubscribe?.(); cards.forEach(c => c.destroy()); cards.clear(); }
  };
}
