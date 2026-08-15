import { matchAdapter } from '../adapters';
import type { FieldSelector, FileFieldSelector } from '../adapters';
import type { AutofillProfile, CanonicalKey, ResolutionTier, UnresolvedField } from '../contracts';
import { resolveByHeuristic } from '../heuristics/matcher';
import { escapeAttributeValue } from '../lib/cssEscape';
import type {
  BackgroundResponse,
  ContentRequest,
  FetchDocumentResponse,
  FieldTierResolution,
  GetProfileResponse,
  ResolveUnresolvedFieldsResponse,
  RunAutofillResponse
} from '../messages';
import { applyFieldValue, fillFileInput } from './fill';
import { AutofillOverlay } from './overlay';
import { FIELD_ID_ATTR, computeFormSignature, scanPage } from './scan';
import type { FieldDescriptor, PlannedFill } from './types';

function toUnresolvedField(descriptor: FieldDescriptor): UnresolvedField {
  return {
    elementId: descriptor.elementId,
    label: descriptor.label,
    name: descriptor.name,
    placeholder: descriptor.placeholder,
    autoComplete: descriptor.autoComplete,
    inputType: descriptor.inputType,
    options: descriptor.options
  };
}

function emptyTierCounts(): Record<ResolutionTier, number> {
  return { adapter: 0, heuristic: 0, learned: 0, model: 0, unresolved: 0 };
}

async function sendToBackground<T extends BackgroundResponse>(message: unknown): Promise<T> {
  return chrome.runtime.sendMessage(message) as Promise<T>;
}

/** Runs the full three-tier cascade against the current page and returns a fill plan. */
async function buildFillPlan(): Promise<{
  plan: PlannedFill[];
  adapterId: string | null;
  tierCounts: Record<ResolutionTier, number>;
}> {
  const adapter = matchAdapter(window.location.href);
  const descriptors = scanPage(document);
  const formSignature = computeFormSignature(descriptors);
  const host = window.location.hostname;

  const resolved = new Map<string, { key: CanonicalKey; tier: ResolutionTier; confidence: number }>();

  // Tier 1 — board adapter. Zero tokens.
  if (adapter) {
    const fieldEntries = Object.entries(adapter.fields) as Array<
      [CanonicalKey, FieldSelector | undefined]
    >;
    for (const [key, fieldSelector] of fieldEntries) {
      if (!fieldSelector) continue;
      const el = document.querySelector(fieldSelector.selector);
      if (!(el instanceof HTMLElement)) continue;
      const elementId = el.getAttribute(FIELD_ID_ATTR);
      if (!elementId || resolved.has(elementId)) continue;
      resolved.set(elementId, { key, tier: 'adapter', confidence: 1 });
    }
  }

  // Tier 2 — heuristic matcher. Zero tokens.
  const afterAdapter = descriptors.filter((d) => !resolved.has(d.elementId));
  const heuristicMatches = resolveByHeuristic(afterAdapter);
  for (const [elementId, match] of heuristicMatches) {
    resolved.set(elementId, { key: match.key, tier: 'heuristic', confidence: match.confidence });
  }

  // Tier 3 — learned field map, then (only if still needed) the model fallback.
  // Both happen inside the background service worker, which owns network access.
  const stillUnresolved = descriptors.filter((d) => !resolved.has(d.elementId));
  if (stillUnresolved.length > 0) {
    try {
      const response = await sendToBackground<ResolveUnresolvedFieldsResponse>({
        type: 'resolve-unresolved-fields',
        host,
        formSignature,
        fields: stillUnresolved.map(toUnresolvedField)
      });
      for (const resolution of response.resolutions as FieldTierResolution[]) {
        if (resolution.canonicalKey) {
          resolved.set(resolution.elementId, {
            key: resolution.canonicalKey,
            tier: resolution.tier,
            confidence: resolution.confidence
          });
        }
      }
    } catch {
      // Background/backend unreachable — tier 3 simply stays unavailable;
      // tiers 1 and 2 results above are unaffected.
    }
  }

  // Pull the profile (cached locally by the service worker, TTL'd) to turn
  // resolved canonical keys into actual values.
  let profile: AutofillProfile | null = null;
  try {
    const profileResponse = await sendToBackground<GetProfileResponse>({ type: 'get-profile' });
    profile = profileResponse.profile;
  } catch {
    profile = null;
  }

  const tierCounts = emptyTierCounts();
  const plan: PlannedFill[] = descriptors.map((descriptor) => {
    const match = resolved.get(descriptor.elementId);
    const tier: ResolutionTier = match?.tier ?? 'unresolved';
    tierCounts[tier] += 1;

    const canonicalKey = match?.key ?? '';
    const value = canonicalKey ? (profile?.fields[canonicalKey] ?? '') : '';

    return {
      elementId: descriptor.elementId,
      label: descriptor.label,
      canonicalKey,
      value: value ?? '',
      tier,
      confidence: match?.confidence ?? 0,
      accepted: tier !== 'unresolved' && Boolean(value)
    };
  });

  // File inputs aren't part of the canonical-key cascade (there's no
  // canonical key for "resume") — a board adapter's `fileFields` is the
  // only tier that can address them today.
  if (adapter?.fileFields && profile) {
    const fileEntries = Object.entries(adapter.fileFields) as Array<
      ['resume' | 'coverLetter', FileFieldSelector | undefined]
    >;
    for (const [kind, fileSelector] of fileEntries) {
      if (!fileSelector) continue;
      const el = document.querySelector(fileSelector.selector);
      if (!(el instanceof HTMLElement)) continue;
      const elementId = el.getAttribute(FIELD_ID_ATTR);
      if (!elementId) continue;
      const doc = profile.documents.find((d) => d.kind === fileSelector.documentKind);
      if (!doc) continue;

      tierCounts.adapter += 1;
      plan.push({
        elementId,
        label: kind === 'resume' ? 'Resume' : 'Cover letter',
        canonicalKey: '',
        value: doc.fileName,
        tier: 'adapter',
        confidence: 1,
        accepted: true,
        file: { documentKind: fileSelector.documentKind, fileName: doc.fileName, downloadUrl: doc.downloadUrl }
      });
    }
  }

  return { plan, adapterId: adapter?.id ?? null, tierCounts };
}

async function applyPlan(accepted: readonly PlannedFill[]): Promise<void> {
  for (const row of accepted) {
    if (row.file) {
      const fileResponse = await sendToBackground<FetchDocumentResponse>({
        type: 'fetch-document',
        downloadUrl: row.file.downloadUrl,
        fileName: row.file.fileName
      });
      if (!fileResponse.ok || !fileResponse.buffer) continue;
      const elements = document.querySelectorAll<HTMLInputElement>(
        `[${FIELD_ID_ATTR}="${escapeAttributeValue(row.elementId)}"]`
      );
      const input = Array.from(elements).find(
        (el): el is HTMLInputElement => el instanceof HTMLInputElement && el.type === 'file'
      );
      if (!input) continue;
      const file = new File([fileResponse.buffer], fileResponse.fileName ?? row.file.fileName, {
        type: fileResponse.mimeType ?? 'application/octet-stream'
      });
      fillFileInput(input, file);
      continue;
    }

    if (!row.canonicalKey || !row.value) continue;
    applyFieldValue(row.elementId, row.value);
  }
}

let overlay: AutofillOverlay | null = null;

async function runAutofill(): Promise<RunAutofillResponse> {
  try {
    const { plan, adapterId, tierCounts } = await buildFillPlan();

    overlay = new AutofillOverlay({
      onApply: (accepted) => {
        void applyPlan(accepted);
      },
      onCancel: () => {
        // Nothing to do — no writes have happened yet.
      }
    });
    overlay.show(plan);

    return {
      type: 'run-autofill-result',
      ok: true,
      adapterId,
      tierCounts,
      fieldCount: plan.length
    };
  } catch (error) {
    return {
      type: 'run-autofill-result',
      ok: false,
      adapterId: null,
      tierCounts: emptyTierCounts(),
      fieldCount: 0,
      error: error instanceof Error ? error.message : 'Unknown error'
    };
  }
}

chrome.runtime.onMessage.addListener((message: ContentRequest, _sender, sendResponse) => {
  if (message.type === 'run-autofill') {
    void runAutofill().then(sendResponse);
    return true;
  }
  return false;
});
