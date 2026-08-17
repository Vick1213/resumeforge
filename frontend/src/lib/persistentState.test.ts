import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { usePersistentState } from '@/lib/persistentState';

type State = Record<string, 'auto' | 'always' | 'never'>;

const KEY = 'test.selection';
const VALID = ['auto', 'always', 'never'];

function revive(raw: unknown): State | undefined {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return undefined;
  return Object.fromEntries(
    Object.entries(raw).filter((entry): entry is [string, State[string]] => VALID.includes(entry[1] as string)),
  );
}

describe('usePersistentState', () => {
  beforeEach(() => {
    window.localStorage.clear();
    vi.restoreAllMocks();
  });

  it('restores a previously stored value instead of the initial one', () => {
    window.localStorage.setItem(KEY, JSON.stringify({ 'prj:a': 'always' }));

    const { result } = renderHook(() => usePersistentState<State>(KEY, {}, revive));

    expect(result.current[0]).toEqual({ 'prj:a': 'always' });
  });

  it('persists updates, including those made with an updater function', () => {
    const { result } = renderHook(() => usePersistentState<State>(KEY, {}, revive));

    act(() => result.current[1]({ 'prj:a': 'never' }));
    act(() => result.current[1]((current) => ({ ...current, 'prj:b': 'always' })));

    expect(JSON.parse(window.localStorage.getItem(KEY) ?? 'null')).toEqual({
      'prj:a': 'never',
      'prj:b': 'always',
    });
  });

  it('falls back to the initial value when storage holds unparseable content', () => {
    window.localStorage.setItem(KEY, 'not json at all');

    const { result } = renderHook(() => usePersistentState<State>(KEY, { 'prj:fallback': 'auto' }, revive));

    expect(result.current[0]).toEqual({ 'prj:fallback': 'auto' });
  });

  it('drops stored entries the reviver rejects rather than adopting the whole map', () => {
    window.localStorage.setItem(KEY, JSON.stringify({ 'prj:a': 'always', 'prj:b': 'sometimes' }));

    const { result } = renderHook(() => usePersistentState<State>(KEY, {}, revive));

    expect(result.current[0]).toEqual({ 'prj:a': 'always' });
  });

  it('keeps working when storage refuses the write', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('QuotaExceededError');
    });

    const { result } = renderHook(() => usePersistentState<State>(KEY, {}, revive));

    expect(() => act(() => result.current[1]({ 'prj:a': 'always' }))).not.toThrow();
    expect(result.current[0]).toEqual({ 'prj:a': 'always' });
  });
});
