import { clearFieldMapCache, getAllCachedFieldMaps } from '../lib/fieldMapCache';
import { getSettings, saveSettings } from '../lib/settings';

async function loadSettingsIntoForm(): Promise<void> {
  const settings = await getSettings();
  const urlInput = document.querySelector<HTMLInputElement>('#backend-url');
  const toggle = document.querySelector<HTMLInputElement>('#allow-model-fallback');
  if (urlInput) urlInput.value = settings.backendBaseUrl;
  if (toggle) toggle.checked = settings.allowModelFallback;
}

async function renderFieldMaps(): Promise<void> {
  const container = document.querySelector<HTMLElement>('#field-map-list');
  if (!container) return;

  const maps = await getAllCachedFieldMaps();
  const hosts = Object.keys(maps).sort();
  container.replaceChildren();

  if (hosts.length === 0) {
    const empty = document.createElement('p');
    empty.className = 'empty';
    empty.textContent = 'No learned field maps cached yet.';
    container.appendChild(empty);
    return;
  }

  for (const host of hosts) {
    const map = maps[host];
    if (!map) continue;

    const card = document.createElement('div');
    card.className = 'field-map-card';

    const title = document.createElement('div');
    title.className = 'field-map-host';
    title.textContent = host;

    const fieldCount = Object.keys(map.elementToKey).length;
    const meta = document.createElement('div');
    meta.className = 'field-map-meta';
    meta.textContent = `${fieldCount} field${fieldCount === 1 ? '' : 's'} learned · used ${map.hitCount} time${
      map.hitCount === 1 ? '' : 's'
    } · last learned ${new Date(map.learnedAt).toLocaleString()}`;

    card.appendChild(title);
    card.appendChild(meta);
    container.appendChild(card);
  }
}

document.querySelector('#settings-form')?.addEventListener('submit', (event) => {
  event.preventDefault();
  void (async () => {
    const urlInput = document.querySelector<HTMLInputElement>('#backend-url');
    const toggle = document.querySelector<HTMLInputElement>('#allow-model-fallback');
    const trimmedUrl = urlInput?.value.trim();

    await saveSettings({
      ...(trimmedUrl ? { backendBaseUrl: trimmedUrl } : {}),
      allowModelFallback: toggle?.checked ?? true
    });

    const status = document.querySelector<HTMLElement>('#save-status');
    if (status) {
      status.textContent = 'Saved.';
      setTimeout(() => {
        status.textContent = '';
      }, 2000);
    }
  })();
});

document.querySelector('#clear-cache-button')?.addEventListener('click', () => {
  void (async () => {
    await clearFieldMapCache();
    await renderFieldMaps();
  })();
});

void loadSettingsIntoForm();
void renderFieldMaps();
