import { ref, set, update, onValue, onDisconnect, serverTimestamp, runTransaction } from './firebase.js';
import { mobileConfig } from '../mobile-config.js';
import { ManualRecorder, mobileState, clipFileName, STALE_MS } from './core.js';
import { VideoPeer } from './peer.js';

const EVENT_NAMES = { connected: 'Conectado', hidden: 'Página oculta', visible: 'Página visible', blur: 'Pérdida de foco', focus: 'Regreso del foco', pagehide: 'Salida de la página', camera_lost: 'Cámara interrumpida', camera_restored: 'Cámara recuperada', connection_lost: 'Sin señal reciente', recording_started: 'Grabación iniciada', recording_stopped: 'Grabación detenida' };
const stamp = ts => ts ? new Date(ts).toLocaleTimeString() : '—';
const element = (tag, className, text) => { const e = document.createElement(tag); e.className = className; if (text) e.textContent = text; return e; };
const button = (text, action) => { const b = element('button', '', text); b.type = 'button'; b.onclick = action; return b; };
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

export function createMobilePanel({ db, auth, container, beep, soundEnabled }) {
  let session = '', active = false, clients = {}, data = {}, unsubscribe, offset = 0, dbOnline = false;
  let epoch = 0, disposed = false, warned = '', cleaned = '', cleaning = false;
  const cards = new Map();
  const now = () => Date.now() + offset;
  const randomToken = () => Array.from(crypto.getRandomValues(new Uint8Array(24)), n => n.toString(16).padStart(2, '0')).join('');
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
      this.videoPlaceholder = element('p', 'mobile-video-placeholder', 'Sin imagen en directo. Activá la cámara en el celular y pulsá «Ver cámara».');
      this.recording = element('p', 'mobile-recording', '● Grabando cámara móvil'); this.recording.hidden = true;
      this.actions = element('div', 'mobile-actions');
      this.pairButton = button('Generar QR', () => this.pair());
      this.viewButton = button('Ver cámara', () => this.view());
      this.expandButton = button('Ampliar imagen', () => this.toggleSize());
      this.expandButton.setAttribute('aria-pressed', 'false');
      this.startButton = button('Grabar', () => this.start());
      this.stopButton = button('Detener grabación', () => this.stop('manual'));
      this.closeButton = button('Cerrar video', () => this.disconnect('viewer_closed'));
      this.stopButton.className = 'mobile-danger';
      this.actions.append(this.expandButton, this.pairButton, this.viewButton, this.startButton, this.stopButton, this.closeButton);
      this.qr = element('div', 'mobile-qr');
      this.message = element('p', 'mobile-message');
      this.events = element('div', 'mobile-events');
      this.clips = element('div');
      this.root.append(this.name, this.state, this.videoPlaceholder, this.video, this.recording, this.actions, this.qr, this.message, this.events, this.clips);
      container.append(this.root);
      this.recorder = new ManualRecorder({ onComplete: clip => this.complete(clip), onError: error => this.say(error.message), onLimit: text => this.say(text) });
    }
    get path() { return `mobileSessions/${this.session}`; }
    get rtc() { return `${this.path}/rtc/${this.token}`; }
    get busy() { return this.starting || this.recorder.state !== 'idle' || this.pendingClips.size > 0; }
    say(text) { this.message.textContent = text; }
    toggleSize() {
      const expanded = this.root.classList.toggle('mobile-expanded');
      this.expandButton.textContent = expanded ? 'Reducir imagen' : 'Ampliar imagen';
      this.expandButton.setAttribute('aria-pressed', String(expanded));
    }
    async log(type) {
      if (!this.token || !dbOnline) return;
      const id = `${type}_${this.id || 'panel'}_${Math.floor(now() / 2000)}`.slice(0, 80);
      try { await set(ref(db, `${this.path}/events/${this.token}/${id}`), { type, ts: serverTimestamp(), source: 'teacher' }); } catch { }
    }
    refresh() {
      const client = clients[this.studentUid];
      this.name.textContent = `Cámara móvil · ${client?.name || this.studentUid}`;
      const token = data.links?.[this.studentUid]?.token || '';
      if (token !== this.token) { this.disconnect('pair_replaced'); this.token = token; this.qr.replaceChildren(); delete this.qr.dataset.token; }
      const pair = data.pairs?.[this.token], status = data.status?.[this.token];
      if (this.qrInfo?.token === this.token && this.qr.dataset.token !== this.token) this.renderQr();
      if (pair?.claimedBy) this.qr.replaceChildren(element('p', 'mobile-note', 'Celular vinculado.'));
      else if (pair && pair.inviteExpiresAt <= now()) this.qr.replaceChildren(element('p', 'mobile-note', 'El QR venció. Generá uno nuevo.'));
      this.status = status;
      const ended = !active || !client || client.state === 'finished' || pair?.revoked || (pair && pair.expiresAt <= now());
      if (ended) this.disconnect('evaluation_finished');
      const supervisionLive = this.live || this.graceUntil > now();
      const state = ended ? { level: 'pending', text: 'Cámara finalizada' } : mobileState(status, now(), supervisionLive);
      if (!ended && this.connectionFailed && !this.live && state.level === 'pending') {
        state.level = 'warning'; state.text = 'Video directo no disponible en esta red';
      }
      if (!dbOnline) { state.level = 'warning'; state.text = 'Tu panel está sin conexión'; }
      this.state.textContent = this.recorder.state !== 'idle' ? '● Grabando cámara móvil' : state.text;
      this.state.dataset.level = state.level;
      this.video.hidden = !(this.live && this.video.videoWidth > 0);
      this.videoPlaceholder.hidden = !this.video.hidden;
      this.videoPlaceholder.textContent = this.connecting || (this.peer && !this.live)
        ? 'Conectando video del celular…' : 'Sin imagen en directo. Activá la cámara en el celular y pulsá «Ver cámara».';
      this.recording.hidden = this.recorder.state === 'idle';
      const lease = data.rtc?.[this.token]?.viewer;
      if (this.peer && ((lease?.id && lease.id !== this.id && lease.expiresAt > now()) || this.leaseExpiresAt <= now())) this.disconnect('viewer_lease_lost');
      if (this.recorder.state === 'recording') {
        if (status?.recordingAck === this.clipId) this.lastAckAt = now();
        // Heartbeats can arrive out of order. Give the mobile page a short grace
        // period before finalizing a clip, while still closing it on a real loss.
        const ackExpired = !this.lastAckAt || now() - this.lastAckAt > 5000;
        if (state.level !== 'ok' || ackExpired) this.stop('supervision_interrupted');
      }
      if (this.warning !== state.text && state.level === 'warning' && status && dbOnline) {
        if (soundEnabled()) beep();
        if (state.text.includes('sin señal')) this.log('connection_lost');
      }
      this.warning = state.text;
      this.pairButton.disabled = ended || this.busy || !!this.pairing;
      this.viewButton.disabled = ended || !pair?.claimedBy || !!this.peer || !!this.connecting || !dbOnline;
      this.startButton.disabled = ended || state.level !== 'ok' || this.busy || !dbOnline || !this.video.videoWidth;
      this.stopButton.disabled = this.recorder.state !== 'recording' && !this.starting;
      this.closeButton.disabled = !this.peer && !this.connecting;
      const eventList = Object.values(data.events?.[this.token] || {}).sort((a, b) => b.ts - a.ts).slice(0, 8);
      this.events.replaceChildren(...eventList.map(e => element('p', '', `${stamp(e.ts)} · ${EVENT_NAMES[e.type] || e.type}`)));
    }
    async pair() {
      if (this.busy || this.pairing) return;
      this.pairing = true; this.refresh();
      try {
        const token = randomToken(), createdAt = now(), previous = this.token;
        const updates = {
          [`pairs/${token}`]: { studentUid: this.studentUid, claimedBy: '', revoked: false, createdAt,
            inviteExpiresAt: createdAt + 5 * 60 * 1000, expiresAt: createdAt + 24 * 60 * 60 * 1000 },
          [`links/${this.studentUid}`]: { token, inviteExpiresAt: createdAt + 5 * 60 * 1000 }
        };
        if (previous) updates[`pairs/${previous}/revoked`] = true;
        await update(ref(db, this.path), updates);
        if (this.session !== session) return;
        const url = new URL('celular.html', location.href); url.hash = new URLSearchParams({ session: this.session, token });
        this.qrInfo = { token, url: url.href };
        this.renderQr();
        this.say('QR válido por 5 minutos y para un solo teléfono. También aparece al pulsar «Vincular celular» en la app Windows actualizada.');
      } catch (error) { this.say(error.message || 'No se pudo crear el QR. Verificá las reglas de la base de datos.'); }
      finally { this.pairing = false; this.refresh(); }
    }
    renderQr() {
      const { token, url } = this.qrInfo;
      this.qr.replaceChildren(); this.qr.dataset.token = token;
      new window.QRCode(this.qr, { text: url, width: 220, height: 220, correctLevel: window.QRCode.CorrectLevel.M });
      this.qr.append(button('Copiar enlace individual', async () => {
        try { await navigator.clipboard.writeText(url); this.say('Enlace copiado. Compartilo solo con este estudiante.'); }
        catch { this.say(url); }
      }));
    }
    async view() {
      if (this.peer || this.connecting || !this.token) return;
      this.connecting = true; this.connectionFailed = false; const generation = ++this.generation; this.id = crypto.randomUUID();
      const id = this.id, rtc = this.rtc;
      try {
        const result = await runTransaction(ref(db, `${rtc}/viewer`), current => {
          if (current && current.expiresAt > now() && current.id !== id) return;
          return { id, uid: auth.currentUser.uid, expiresAt: now() + 25000 };
        }, { applyLocally: false });
        if (!result.committed) throw new Error('Esta cámara está abierta en otro panel. Cerralo o esperá 25 segundos.');
        this.leaseExpiresAt = result.snapshot.val().expiresAt;
        const { iceServers } = mobileConfig;
        if (generation !== this.generation || this.session !== session) return;
        const off = { id: '', active: false, startedAt: 0, viewerId: id };
        await onDisconnect(ref(db, `${rtc}/recording`)).set(off);
        await set(ref(db, `${rtc}/recording`), off);
        this.peer = new VideoPeer({ db, path: rtc, id, iceServers, role: 'teacher',
          onStream: stream => { this.video.srcObject = stream; this.stream = stream; this.video.onloadeddata = () => this.refresh(); this.video.play().catch(() => this.say('Pulsá el video para reproducirlo.')); },
          onState: state => {
            if (state === 'connected') { this.live = true; this.graceUntil = 0; clearTimeout(this.connectionTimer); }
            else if (state === 'disconnected') {
              this.graceUntil = now() + 5000;
              clearTimeout(this.connectionTimer);
              this.connectionTimer = setTimeout(() => {
                if (this.peer && this.graceUntil <= now()) { this.live = false; this.connectionFailed = true; this.stop('video_interrupted'); this.say('Video interrumpido. Cerrá el video y pulsá «Ver cámara» para reconectar.'); this.refresh(); }
              }, 5200);
            } else if (['failed', 'closed'].includes(state)) {
              this.live = false; this.connectionFailed = true; this.stop('video_interrupted'); this.say('No se pudo mantener la conexión directa. Cerrá el video y reintentá; si esta red lo impide, usá la segunda cámara de Meet.');
            }
            this.refresh();
          }, onError: error => this.say(`Conexión de video: ${error.message || 'no disponible'}`) });
        this.video.onclick = () => this.video.play().catch(() => {});
        await this.peer.offer();
        this.connectionTimer = setTimeout(() => {
          if (this.peer && !this.live) {
            this.connectionFailed = true;
            this.disconnect('direct_connection_failed');
            this.say('No se logró una conexión directa en esta red. Podés reintentar o usar la segunda cámara de Meet.');
            this.refresh();
          }
        }, 20000);
        this.leaseTimer = setInterval(async () => {
          try { const expiresAt = now() + 25000; await set(ref(db, `${rtc}/viewer`), { id, uid: auth.currentUser.uid, expiresAt }); this.leaseExpiresAt = expiresAt; }
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
        // The phone confirms camera and connection state before local capture starts.
        const deadline = Date.now() + 6000;
        while (this.status?.recordingAck !== id && Date.now() < deadline && !this.cancelStart && dbOnline && this.live) await delay(100);
        if (this.cancelStart || !dbOnline || !this.live || this.status?.recordingAck !== id || token !== this.token) throw new Error('El celular no confirmó la conexión. No se inició la grabación.');
        const startedAt = now();
        await set(ref(db, `${this.path}/recordings/${token}/${id}`), { teacherUid: auth.currentUser.uid, startedAt, acknowledgedAt: serverTimestamp() });
        if (this.cancelStart || !this.live) throw new Error('Grabación cancelada antes de comenzar.');
        const captureStream = this.video.captureStream?.() || this.video.srcObject || this.stream;
        this.recorder.start(captureStream, { id, token, session: this.session, studentUid: this.studentUid, startedAt });
        this.lastAckAt = now();
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
      this.stop(reason); clearInterval(this.leaseTimer); clearTimeout(this.connectionTimer);
      this.peer?.close(); this.peer = null; this.live = false; this.stream = null; this.video.srcObject = null;
    }
    complete(clip) {
      // Keep only the incident metadata in RTDB. The video remains in this browser
      // until the teacher explicitly downloads or discards it.
      clip.endedAt += offset;
      if (!clip.blob.size) { this.say('No se recibieron imágenes para guardar.'); return; }
      const row = element('div', 'mobile-clip');
      const state = element('p', '', `Clip ${stamp(clip.startedAt)}–${stamp(clip.endedAt)} · pendiente de descarga`);
      const download = button('Descargar clip', () => {
        const a = document.createElement('a'), url = URL.createObjectURL(clip.blob); this.urls.add(url);
        a.href = url; a.download = clipFileName(clip.session, clip.studentUid, clip.startedAt, clip.blob.type); a.click();
        this.pendingClips.delete(clip.id);
        state.textContent = 'Descarga iniciada. Comprobá el archivo antes de cerrar el panel.';
        download.disabled = true; discard.remove();
        this.say('El clip queda en esta computadora. Guardalo según las normas de UTEC.');
        this.refresh();
      });
      const discard = button('Descartar copia pendiente', () => { if (confirm('¿Descartar este clip pendiente?')) { this.pendingClips.delete(clip.id); row.remove(); this.refresh(); } });
      row.append(state, download, discard); this.clips.prepend(row);
      this.pendingClips.set(clip.id, { clip, row });
      this.refresh();
    }
    destroy() { this.disconnect('panel_closed'); this.urls.forEach(url => URL.revokeObjectURL(url)); this.root.remove(); }
  }

  function reconcile() {
    if (disposed) return;
    for (const studentUid of Object.keys(clients)) { if (!cards.has(studentUid)) cards.set(studentUid, new CameraCard(studentUid)); }
    cards.forEach(card => card.refresh());
    // No paid scheduler: a teacher panel removes temporary metadata when a
    // finished evaluation is next opened. A forcibly abandoned session needs
    // a later teacher visit for physical deletion.
    if (session && !active && cleaned !== session && !cleaning && ![...cards.values()].some(c => c.busy)) {
      const closing = session;
      cleaning = true;
      update(ref(db), { [`mobileSessions/${closing}`]: null, [`mobileMembers/${closing}`]: null })
        .then(() => { if (session === closing) cleaned = closing; })
        .catch(() => report('No se pudieron borrar los datos móviles de esta sesión. Volvé a abrir el panel docente para reintentar.'))
        .finally(() => { cleaning = false; });
    }
  }
  const interval = setInterval(reconcile, 1000);
  window.addEventListener('beforeunload', e => {
    if ([...cards.values()].some(c => c.busy)) { e.preventDefault(); e.returnValue = ''; }
  });
  window.addEventListener('pagehide', () => cards.forEach(c => c.disconnect('panel_closed')));
  return {
    getStatus(studentUid) {
      const card = cards.get(studentUid);
      if (!card) return null;
      const status = card.status;
      const connected = !!status && status.connected === true && now() - Number(status.lastSeen || 0) < STALE_MS;
      return { connected, live: card.live === true };
    },
    canLeave() {
      if (![...cards.values()].some(c => c.busy)) return true;
      report('Detené las grabaciones y descargá o descartá los clips pendientes antes de cambiar de sesión o salir.');
      return false;
    },
    setContext(next) {
      active = next.active; clients = next.clients || {};
      if (active) cleaned = '';
      if (session !== next.session) {
        if (unsubscribe) unsubscribe(); epoch++;
        cards.forEach(c => c.destroy()); cards.clear(); data = {}; session = next.session; warned = ''; cleaned = '';
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

