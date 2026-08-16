import type { ModelEffort } from '../contracts';

/** Extension-wide settings, persisted in `chrome.storage.local`. */
export interface ExtensionSettings {
  backendBaseUrl: string;
  /**
   * Whether tier 3 (the model fallback) is allowed to run at all. This is
   * the ONLY tier that spends tokens — tiers 1 and 2 are always free and
   * always run regardless of this setting.
   */
  allowModelFallback: boolean;
  /**
   * How much of the form gets handed to the model when tier 3 runs
   * (CONTRACTS.md §10). Raising it raises the tier-2 heuristic's accept
   * threshold too, so the free matcher keeps only what it's confident about
   * and defers more to the model; at Thorough and above the model also
   * drafts free-text answers for open questions. Spent tokens are still
   * bounded by `allowModelFallback`, and once a form is learned, resolving
   * it again is free regardless of this setting.
   */
  effort: ModelEffort;
}

export const DEFAULT_SETTINGS: ExtensionSettings = {
  backendBaseUrl: 'http://localhost:5217',
  allowModelFallback: true,
  effort: 'standard'
};

const SETTINGS_KEY = 'resumeforge.settings';

export async function getSettings(): Promise<ExtensionSettings> {
  const stored = await chrome.storage.local.get(SETTINGS_KEY);
  const value = stored[SETTINGS_KEY] as Partial<ExtensionSettings> | undefined;
  return { ...DEFAULT_SETTINGS, ...value };
}

export async function saveSettings(
  patch: Partial<ExtensionSettings>
): Promise<ExtensionSettings> {
  const current = await getSettings();
  const next: ExtensionSettings = { ...current, ...patch };
  await chrome.storage.local.set({ [SETTINGS_KEY]: next });
  return next;
}
