import type { ModelEffort } from '../contracts';
import { clearFieldMapCache, getAllCachedFieldMaps } from '../lib/fieldMapCache';
import { getSettings, saveSettings } from '../lib/settings';

/**
 * Framing straight from CONTRACTS.md §10's autofill effort table: each level
 * names the tier-2 accept threshold it raises the heuristic matcher to, and
 * what extra work that leaves for the (token-spending) model fallback.
 */
const EFFORT_DESCRIPTIONS: Record<ModelEffort, string> = {
  minimal:
    'The heuristic matcher accepts anything at 60% confidence and up, so almost every field resolves for free. ' +
    'Cheapest, and the least thorough.',
  standard:
    'The heuristic matcher accepts matches at 72% confidence and up (the default). Fields it is not confident ' +
    "about, including choosing among select/radio options, go to the model — free once the form is learned.",
  thorough:
    'The heuristic matcher only keeps matches at 80% confidence and up, handing more of the form to the model — ' +
    'which now also drafts free-text answers for open questions ("why this role", etc.), grounded in your ' +
    'knowledge base.',
  maximum:
    'The heuristic matcher only keeps matches at 88% confidence and up. The model drafts free-text answers with a ' +
    'longer budget per answer. Spends the most tokens on a first visit — a learned form is still free forever after.'
};

function isModelEffort(value: string): value is ModelEffort {
  return value === 'minimal' || value === 'standard' || value === 'thorough' || value === 'maximum';
}

function renderEffortDescription(effort: ModelEffort): void {
  const description = document.querySelector<HTMLElement>('#effort-description');
  if (description) description.textContent = EFFORT_DESCRIPTIONS[effort];
}

async function loadSettingsIntoForm(): Promise<void> {
  const settings = await getSettings();
  const urlInput = document.querySelector<HTMLInputElement>('#backend-url');
  const toggle = document.querySelector<HTMLInputElement>('#allow-model-fallback');
  const effortSelect = document.querySelector<HTMLSelectElement>('#effort-select');
  if (urlInput) urlInput.value = settings.backendBaseUrl;
  if (toggle) toggle.checked = settings.allowModelFallback;
  if (effortSelect) effortSelect.value = settings.effort;
  renderEffortDescription(settings.effort);
}

document.querySelector('#effort-select')?.addEventListener('change', (event) => {
  const value = (event.target as HTMLSelectElement).value;
  if (isModelEffort(value)) renderEffortDescription(value);
});

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
    } · learned at ${map.learnedAtEffort ?? 'standard'} effort · last learned ${new Date(
      map.learnedAt
    ).toLocaleString()}`;

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
    const effortSelect = document.querySelector<HTMLSelectElement>('#effort-select');
    const trimmedUrl = urlInput?.value.trim();
    const effortValue = effortSelect?.value ?? '';

    await saveSettings({
      ...(trimmedUrl ? { backendBaseUrl: trimmedUrl } : {}),
      allowModelFallback: toggle?.checked ?? true,
      ...(isModelEffort(effortValue) ? { effort: effortValue } : {})
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
