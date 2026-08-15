import type { AutofillProfile } from '../contracts';

const PROFILE_CACHE_KEY = 'resumeforge.profileCache';
const PROFILE_TTL_MS = 30 * 60 * 1000; // 30 minutes

interface CachedProfile {
  profile: AutofillProfile;
  cachedAt: number;
}

async function readCacheEntry(): Promise<CachedProfile | null> {
  const stored = await chrome.storage.local.get(PROFILE_CACHE_KEY);
  return (stored[PROFILE_CACHE_KEY] as CachedProfile | undefined) ?? null;
}

/** Returns the cached profile only if it's still within the TTL window. */
export async function getFreshCachedProfile(): Promise<AutofillProfile | null> {
  const entry = await readCacheEntry();
  if (!entry) return null;
  if (Date.now() - entry.cachedAt > PROFILE_TTL_MS) return null;
  return entry.profile;
}

/**
 * Returns the cached profile regardless of age. Used as a fallback when the
 * backend is unreachable, so tiers 1 and 2 keep working offline with
 * whatever profile was last fetched.
 */
export async function getStaleCachedProfile(): Promise<AutofillProfile | null> {
  const entry = await readCacheEntry();
  return entry?.profile ?? null;
}

export async function setCachedProfile(profile: AutofillProfile): Promise<void> {
  const entry: CachedProfile = { profile, cachedAt: Date.now() };
  await chrome.storage.local.set({ [PROFILE_CACHE_KEY]: entry });
}

export async function clearProfileCache(): Promise<void> {
  await chrome.storage.local.remove(PROFILE_CACHE_KEY);
}
