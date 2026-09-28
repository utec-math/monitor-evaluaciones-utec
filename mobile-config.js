// Video is peer-to-peer. STUN only helps discover a direct route; it never relays media.
// Some networks will not connect without a TURN relay. This no-billing pilot accepts that limit.
export const mobileConfig = Object.freeze({
  heartbeatMs: 5000,
  staleMs: 20000,
  iceServers: Object.freeze([{ urls: 'stun:stun.l.google.com:19302' }])
});
