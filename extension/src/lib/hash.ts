/**
 * FNV-1a, 32-bit, hex output. Hand-rolled so `formSignature` doesn't need a
 * crypto dependency — this only needs to be a stable, well-distributed
 * fingerprint, not cryptographically secure.
 */
export function fnv1a(input: string): string {
  let hash = 0x811c9dc5;
  for (let i = 0; i < input.length; i++) {
    hash ^= input.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193);
  }
  // Force unsigned 32-bit, then hex-pad to a fixed 8 characters.
  return (hash >>> 0).toString(16).padStart(8, '0');
}
