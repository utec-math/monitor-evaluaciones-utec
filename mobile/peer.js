import { ref, set, onValue, onChildAdded } from './firebase.js';

// Each viewing attempt has a new ID. ICE from earlier attempts is never reused.
export class VideoPeer {
  constructor({ db, path, id, iceServers, role, stream, onStream, onState, onError }) {
    Object.assign(this, { db, path, id, role, onError });
    this.closed = false;
    this.unsub = [];
    this.pending = [];
    this.iceCount = 0;
    this.pc = new RTCPeerConnection({ iceServers });
    this.pc.onconnectionstatechange = () => onState?.(this.pc.connectionState);
    this.pc.ontrack = e => onStream?.(e.streams[0] || new MediaStream([e.track]));
    this.pc.onicecandidate = e => {
      if (e.candidate && !this.closed && this.iceCount < 64) set(ref(db, `${path}/${role}Ice/${id}/${++this.iceCount}`), JSON.stringify(e.candidate.toJSON())).catch(onError);
    };
    if (stream) stream.getVideoTracks().forEach(track => this.pc.addTrack(track, stream));
    else this.pc.addTransceiver('video', { direction: 'recvonly' });
    const remote = role === 'teacher' ? 'mobile' : 'teacher';
    this.unsub.push(onChildAdded(ref(db, `${path}/${remote}Ice/${id}`), snap => {
      try {
        const candidate = JSON.parse(snap.val());
        if (this.pc.remoteDescription) this.pc.addIceCandidate(candidate).catch(onError);
        else this.pending.push(candidate);
      } catch (error) { onError(error); }
    }, onError));
  }
  async remote(description) {
    if (this.closed) return;
    await this.pc.setRemoteDescription(description);
    for (const candidate of this.pending.splice(0)) await this.pc.addIceCandidate(candidate);
  }
  async offer() {
    await this.pc.setLocalDescription(await this.pc.createOffer());
    if (this.closed) return;
    await set(ref(this.db, `${this.path}/offer`), { id: this.id, sdp: this.pc.localDescription.sdp });
    this.unsub.push(onValue(ref(this.db, `${this.path}/answer`), snap => {
      const value = snap.val();
      if (value?.id === this.id && !this.pc.currentRemoteDescription && !this.closed)
        this.remote({ type: 'answer', sdp: value.sdp }).catch(this.onError);
    }, this.onError));
  }
  async answer(offer) {
    await this.remote({ type: 'offer', sdp: offer.sdp });
    if (this.closed) return;
    await this.pc.setLocalDescription(await this.pc.createAnswer());
    await set(ref(this.db, `${this.path}/answer`), { id: this.id, sdp: this.pc.localDescription.sdp });
  }
  close() {
    this.closed = true;
    this.unsub.splice(0).forEach(fn => fn());
    this.pc.onconnectionstatechange = null;
    this.pc.onicecandidate = null;
    this.pc.close();
    this.pending = [];
  }
}
