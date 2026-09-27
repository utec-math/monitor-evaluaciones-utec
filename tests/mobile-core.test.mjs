import test from 'node:test';
import assert from 'node:assert/strict';
import { ManualRecorder, mobileState, recordingMime, MAX_CLIP_BYTES, validKey } from '../mobile/core.js';

class FakeRecorder {
  static instances = [];
  static isTypeSupported(type) { return type === 'video/webm'; }
  constructor(stream, options) { this.state = 'inactive'; this.mimeType = options.mimeType; FakeRecorder.instances.push(this); }
  start() { this.state = 'recording'; }
  stop() { this.state = 'inactive'; this.ondataavailable({ data: new Blob(['last-frame']) }); queueMicrotask(() => this.onstop()); }
}
const stream = { getVideoTracks: () => [{ readyState: 'live', muted: false }] };

test('no recorder or capture is created while only watching', () => {
  FakeRecorder.instances = [];
  const recorder = new ManualRecorder({ Recorder: FakeRecorder });
  assert.equal(recorder.state, 'idle'); assert.equal(FakeRecorder.instances.length, 0);
});
test('explicit start and stop preserve final data and event metadata', async () => {
  let result;
  const recorder = new ManualRecorder({ Recorder: FakeRecorder, onComplete: value => result = value });
  recorder.start(stream, { startedAt: 100, studentUid: 'student', id: 'clip' });
  assert.throws(() => recorder.start(stream, {}), /curso/);
  recorder.recorder.ondataavailable({ data: new Blob(['first-frame']) });
  await recorder.stop('video_interrupted');
  assert.equal(await result.blob.text(), 'first-framelast-frame');
  assert.equal(result.studentUid, 'student'); assert.equal(result.reason, 'video_interrupted');
  assert.equal(recorder.state, 'idle');
});
test('size limit stops the recording and keeps a recoverable result', async () => {
  let limited = false, result;
  const recorder = new ManualRecorder({ Recorder: FakeRecorder, onComplete: v => result = v, onLimit: () => limited = true });
  recorder.start(stream, {});
  recorder.recorder.ondataavailable({ data: new Blob([new Uint8Array(MAX_CLIP_BYTES)]) });
  await recorder.done;
  assert.ok(limited); assert.equal(result.reason, 'size_limit'); assert.ok(result.blob.size >= MAX_CLIP_BYTES);
});
test('ended or muted video cannot start a recording', () => {
  const recorder = new ManualRecorder({ Recorder: FakeRecorder });
  assert.throws(() => recorder.start({ getVideoTracks: () => [{ readyState: 'ended' }] }, {}), /video/);
  assert.throws(() => recorder.start({ getVideoTracks: () => [{ readyState: 'live', muted: true }] }, {}), /video/);
  assert.equal(recordingMime({ isTypeSupported: () => false }), '');
});
test('fresh heartbeat without real video is not labelled live', () => {
  const status = { connected: true, lastSeen: 1000, visible: true, camera: true };
  assert.equal(mobileState(status, 1001, false).level, 'pending');
  assert.equal(mobileState(status, 1001, true).level, 'ok');
  assert.equal(mobileState(status, 22000, true).level, 'warning');
  assert.equal(mobileState({ ...status, visible: false }, 1001, true).text, 'Página del celular oculta');
});
test('QR identifiers reject paths and special characters', () => {
  assert.ok(validKey('EVAL-TEST_1')); assert.ok(!validKey('../other')); assert.ok(!validKey(null));
});
