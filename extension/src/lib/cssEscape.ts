/**
 * Escapes a value for use inside a CSS attribute selector (`[attr="..."]`).
 * Prefers the platform `CSS.escape` (every real browser has it); falls back
 * to a manual escape so this also works in jsdom-based unit tests, where
 * the global `CSS` object isn't guaranteed to be present.
 */
export function escapeAttributeValue(value: string): string {
  if (typeof CSS !== 'undefined' && typeof CSS.escape === 'function') {
    return CSS.escape(value);
  }
  return value.replace(/[^a-zA-Z0-9_-]/g, (ch) => `\\${ch}`);
}
