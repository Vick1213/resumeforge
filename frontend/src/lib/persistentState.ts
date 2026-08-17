import { useCallback, useState } from 'react';

/**
 * `useState` that survives reloads by mirroring into `localStorage`.
 *
 * Storage is treated as untrusted input, not as a typed store: anything that
 * fails to parse, or that `revive` rejects, falls back to the initial value
 * rather than propagating a malformed shape into the component tree. A user who
 * has hand-edited storage, or a build whose stored shape has since changed,
 * therefore gets the default rather than a crash on mount.
 *
 * Writes are best-effort. Private-browsing modes and full quotas both throw on
 * `setItem`, and neither is worth failing a render over — the value simply stops
 * outliving the session.
 */
export function usePersistentState<T>(
  key: string,
  initial: T,
  revive: (raw: unknown) => T | undefined,
): [T, (next: T | ((current: T) => T)) => void] {
  const [value, setValue] = useState<T>(() => read(key, revive) ?? initial);

  const set = useCallback(
    (next: T | ((current: T) => T)) => {
      setValue((current) => {
        const resolved = typeof next === 'function' ? (next as (c: T) => T)(current) : next;
        write(key, resolved);
        return resolved;
      });
    },
    [key],
  );

  return [value, set];
}

function read<T>(key: string, revive: (raw: unknown) => T | undefined): T | undefined {
  try {
    const stored = window.localStorage.getItem(key);
    return stored === null ? undefined : revive(JSON.parse(stored));
  } catch {
    return undefined;
  }
}

function write(key: string, value: unknown): void {
  try {
    window.localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Best-effort; see the doc comment.
  }
}
