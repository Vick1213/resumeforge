import type { LearnedFieldMap } from '../contracts';

const FIELD_MAP_CACHE_KEY = 'resumeforge.fieldMapCache';

type FieldMapsByHost = Record<string, LearnedFieldMap>;

async function readAll(): Promise<FieldMapsByHost> {
  const stored = await chrome.storage.local.get(FIELD_MAP_CACHE_KEY);
  return (stored[FIELD_MAP_CACHE_KEY] as FieldMapsByHost | undefined) ?? {};
}

/** Every learned field map cached locally, for the options page viewer. */
export async function getAllCachedFieldMaps(): Promise<FieldMapsByHost> {
  return readAll();
}

export async function getCachedFieldMap(host: string): Promise<LearnedFieldMap | null> {
  const all = await readAll();
  return all[host] ?? null;
}

export async function setCachedFieldMap(map: LearnedFieldMap): Promise<void> {
  const all = await readAll();
  all[map.host] = map;
  await chrome.storage.local.set({ [FIELD_MAP_CACHE_KEY]: all });
}

export async function clearFieldMapCache(): Promise<void> {
  await chrome.storage.local.remove(FIELD_MAP_CACHE_KEY);
}
