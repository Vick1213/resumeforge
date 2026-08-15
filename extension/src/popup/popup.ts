import { matchAdapter } from '../adapters';
import type { ResolutionTier } from '../contracts';
import type { GetConnectionStatusResponse, RunAutofillResponse } from '../messages';

const TIER_ORDER: ResolutionTier[] = ['adapter', 'heuristic', 'learned', 'model', 'unresolved'];

async function getActiveTab(): Promise<chrome.tabs.Tab | undefined> {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  return tab;
}

async function renderConnectionStatus(): Promise<void> {
  const statusEl = document.querySelector<HTMLElement>('#connection-status');
  if (!statusEl) return;
  try {
    const response = (await chrome.runtime.sendMessage({
      type: 'get-connection-status'
    })) as GetConnectionStatusResponse;
    statusEl.textContent = response.reachable ? 'Connected' : 'Unreachable';
    statusEl.className = response.reachable ? 'status-ok' : 'status-bad';
    statusEl.title = response.backendBaseUrl;
  } catch {
    statusEl.textContent = 'Unavailable';
    statusEl.className = 'status-bad';
  }
}

async function renderAdapterInfo(): Promise<void> {
  const el = document.querySelector<HTMLElement>('#adapter-info');
  if (!el) return;
  const tab = await getActiveTab();
  const adapter = tab?.url ? matchAdapter(tab.url) : undefined;
  el.textContent = adapter
    ? `Board adapter: ${adapter.id}`
    : 'No board adapter for this page — heuristics and the model fallback will handle it.';
}

function renderTierCounts(counts: Record<ResolutionTier, number> | null): void {
  const el = document.querySelector<HTMLElement>('#tier-counts');
  if (!el) return;
  el.replaceChildren();
  if (!counts) {
    el.textContent = 'No scan run yet.';
    return;
  }
  for (const tier of TIER_ORDER) {
    const row = document.createElement('div');
    row.className = 'tier-row';
    const label = document.createElement('span');
    label.textContent = tier;
    const count = document.createElement('span');
    count.textContent = String(counts[tier]);
    row.appendChild(label);
    row.appendChild(count);
    el.appendChild(row);
  }
}

async function runScanAndFill(): Promise<void> {
  const button = document.querySelector<HTMLButtonElement>('#scan-button');
  const resultEl = document.querySelector<HTMLElement>('#scan-result');
  const tab = await getActiveTab();

  if (!tab?.id) {
    if (resultEl) resultEl.textContent = 'No active tab.';
    return;
  }

  if (button) button.disabled = true;
  if (resultEl) resultEl.textContent = 'Scanning…';

  try {
    const response = (await chrome.tabs.sendMessage(tab.id, {
      type: 'run-autofill'
    })) as RunAutofillResponse;

    if (response.ok) {
      renderTierCounts(response.tierCounts);
      if (resultEl) {
        resultEl.textContent =
          response.fieldCount === 0
            ? 'No fillable fields found on this page.'
            : `Found ${response.fieldCount} field(s) — review the on-page panel to apply.`;
      }
    } else if (resultEl) {
      resultEl.textContent = `Scan failed: ${response.error ?? 'unknown error'}`;
    }
  } catch {
    if (resultEl) {
      resultEl.textContent = 'Could not reach this page. Reload the tab and try again.';
    }
  } finally {
    if (button) button.disabled = false;
  }
}

document.querySelector('#options-link')?.addEventListener('click', (event) => {
  event.preventDefault();
  chrome.runtime.openOptionsPage();
});

document.querySelector('#scan-button')?.addEventListener('click', () => {
  void runScanAndFill();
});

void renderConnectionStatus();
void renderAdapterInfo();
renderTierCounts(null);
