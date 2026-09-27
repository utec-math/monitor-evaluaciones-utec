import test from 'node:test';
import assert from 'node:assert/strict';
import { canClaim, key, turnCredentials } from '../functions/domain.js';

test('a QR expires, can be claimed once and resumed only by its owner', () => {
  const pair = { claimedBy: '', revoked: false, inviteExpiresAt: 2000, expiresAt: 10000 };
  assert.equal(canClaim(pair, 'a', 1000), true);
  assert.equal(canClaim(pair, 'a', 2000), false);
  assert.equal(canClaim({ ...pair, claimedBy: 'a' }, 'b', 1000), false);
  assert.equal(canClaim({ ...pair, claimedBy: 'a' }, 'a', 3000), true);
  assert.equal(canClaim({ ...pair, claimedBy: 'a' }, 'a', 10000), false);
  assert.equal(canClaim({ ...pair, revoked: true }, 'a', 1000), false);
});
test('TURN credentials expire in an hour and never reveal the shared secret', () => {
  const result = turnCredentials('mobile-1', 'test-only-secret', ['turn:example.invalid'], 1000000);
  assert.equal(result.username, '4600:mobile-1');
  assert.notEqual(result.credential, 'test-only-secret');
  assert.notEqual(result.credential, turnCredentials('mobile-2', 'test-only-secret', result.urls, 1000000).credential);
});
test('backend rejects malformed identifiers', () => {
  for (const value of ['', null, '../../admins', 'one.two', 'x'.repeat(161)]) assert.throws(() => key(value));
  assert.equal(key('a_B-123'), 'a_B-123');
});
