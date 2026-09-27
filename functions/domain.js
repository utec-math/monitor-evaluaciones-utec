import { createHmac } from 'node:crypto';
export const RETENTION_MS = 24 * 60 * 60 * 1000;
export const INVITE_MS = 5 * 60 * 1000;
export function key(value) {
  if (typeof value !== 'string' || !/^[A-Za-z0-9_-]{1,160}$/.test(value)) throw new Error('Identificador inválido');
  return value;
}
export function canClaim(pair, uid, now) {
  return !!pair && !pair.revoked && pair.expiresAt > now &&
    (pair.claimedBy === uid || (!pair.claimedBy && pair.inviteExpiresAt > now));
}
export function turnCredentials(uid, secret, urls, now = Date.now()) {
  const username = `${Math.floor(now / 1000) + 3600}:${uid}`;
  return { urls, username, credential: createHmac('sha1', secret).update(username).digest('base64') };
}
