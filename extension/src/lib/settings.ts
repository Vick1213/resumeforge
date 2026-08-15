/** Extension-wide settings, persisted in `chrome.storage.local`. */
export interface ExtensionSettings {
  backendBaseUrl: string;
  /**
   * Whether tier 3 (the model fallback) is allowed to run at all. This is
   * the ONLY tier that spends tokens — tiers 1 and 2 are always free and
   * always run regardless of this setting.
   */
  allowModelFallback: boolean;
}

export const DEFAULT_SETTINGS: ExtensionSettings = {
  backendBaseUrl: 'http://localhost:5217',
  allowModelFallback: true
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
