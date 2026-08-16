import type {
  AutofillProfile,
  CanonicalKey,
  LearnedFieldMap,
  ResolveFieldsRequest,
  ResolveFieldsResponse,
  UnresolvedField
} from '../contracts';
import { isCanonicalKey } from '../contracts';
import { getCachedFieldMap, setCachedFieldMap } from '../lib/fieldMapCache';
import { getFreshCachedProfile, getStaleCachedProfile, setCachedProfile } from '../lib/profileCache';
import { getSettings } from '../lib/settings';
import type {
  BackgroundRequest,
  BackgroundResponse,
  FetchDocumentRequest,
  FetchDocumentResponse,
  FieldTierResolution,
  GetConnectionStatusResponse,
  GetProfileResponse,
  ResolveUnresolvedFieldsRequest,
  ResolveUnresolvedFieldsResponse
} from '../messages';

/**
 * The service worker is the only part of this extension that ever makes a
 * network call. Content scripts message it for everything backend-shaped:
 * fetching the autofill profile, resolving fields via the model fallback,
 * looking up/persisting learned field maps, and downloading documents to
 * attach to file inputs. That boundary is what makes "backend unreachable"
 * a single, well-contained failure mode instead of something every content
 * script has to handle independently.
 */

const REQUEST_TIMEOUT_MS = 6000;

async function fetchWithTimeout(url: string, init?: RequestInit): Promise<Response> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  try {
    return await fetch(url, { ...init, signal: controller.signal });
  } finally {
    clearTimeout(timeout);
  }
}

function apiUrl(baseUrl: string, path: string): string {
  return `${baseUrl.replace(/\/+$/, '')}${path}`;
}

async function fetchProfileFromBackend(baseUrl: string): Promise<AutofillProfile> {
  const response = await fetchWithTimeout(apiUrl(baseUrl, '/api/autofill/profile'));
  if (!response.ok) {
    throw new Error(`GET /api/autofill/profile failed: ${response.status}`);
  }
  return (await response.json()) as AutofillProfile;
}

async function handleGetProfile(): Promise<GetProfileResponse> {
  const settings = await getSettings();
  const fresh = await getFreshCachedProfile();
  if (fresh) {
    return { type: 'get-profile-result', profile: fresh, fromCache: true, backendUnavailable: false };
  }

  try {
    const profile = await fetchProfileFromBackend(settings.backendBaseUrl);
    await setCachedProfile(profile);
    return { type: 'get-profile-result', profile, fromCache: false, backendUnavailable: false };
  } catch {
    const stale = await getStaleCachedProfile();
    return {
      type: 'get-profile-result',
      profile: stale,
      fromCache: stale !== null,
      backendUnavailable: true
    };
  }
}

async function fetchLearnedFieldMap(baseUrl: string, host: string): Promise<LearnedFieldMap | null> {
  const response = await fetchWithTimeout(
    apiUrl(baseUrl, `/api/autofill/fieldmap/${encodeURIComponent(host)}`)
  );
  if (response.status === 404) return null;
  if (!response.ok) {
    throw new Error(`GET /api/autofill/fieldmap/${host} failed: ${response.status}`);
  }
  const body = (await response.json()) as LearnedFieldMap | null;
  return body ?? null;
}

async function persistLearnedFieldMap(baseUrl: string, map: LearnedFieldMap): Promise<boolean> {
  try {
    const response = await fetchWithTimeout(apiUrl(baseUrl, '/api/autofill/fieldmap'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(map)
    });
    return response.ok;
  } catch {
    return false;
  }
}

async function resolveViaModel(
  baseUrl: string,
  request: ResolveFieldsRequest
): Promise<ResolveFieldsResponse> {
  const response = await fetchWithTimeout(apiUrl(baseUrl, '/api/autofill/resolve'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request)
  });
  if (!response.ok) {
    throw new Error(`POST /api/autofill/resolve failed: ${response.status}`);
  }
  return (await response.json()) as ResolveFieldsResponse;
}

async function handleResolveUnresolvedFields(
  message: ResolveUnresolvedFieldsRequest
): Promise<ResolveUnresolvedFieldsResponse> {
  const settings = await getSettings();
  const resolutions: FieldTierResolution[] = [];

  let learnedMap: LearnedFieldMap | null = null;
  let learnedMapUnavailable = false;
  try {
    learnedMap = await fetchLearnedFieldMap(settings.backendBaseUrl, message.host);
  } catch {
    learnedMap = await getCachedFieldMap(message.host);
    learnedMapUnavailable = true;
  }

  let stillUnresolved: UnresolvedField[] = message.fields;
  if (learnedMap && learnedMap.formSignature === message.formSignature) {
    const unmatched: UnresolvedField[] = [];
    for (const field of message.fields) {
      const key = learnedMap.elementToKey[field.elementId];
      if (key && isCanonicalKey(key)) {
        resolutions.push({ elementId: field.elementId, canonicalKey: key, tier: 'learned', confidence: 1 });
      } else {
        unmatched.push(field);
      }
    }
    stillUnresolved = unmatched;
  }

  if (stillUnresolved.length === 0) {
    return {
      type: 'resolve-unresolved-fields-result',
      resolutions,
      backendUnavailable: learnedMapUnavailable,
      modelFallbackDisabled: false
    };
  }

  if (!settings.allowModelFallback) {
    return {
      type: 'resolve-unresolved-fields-result',
      resolutions,
      backendUnavailable: false,
      modelFallbackDisabled: true
    };
  }

  try {
    const response = await resolveViaModel(settings.backendBaseUrl, {
      host: message.host,
      formSignature: message.formSignature,
      fields: stillUnresolved,
      effort: message.effort
    });

    const newlyResolved: Record<string, CanonicalKey> = {};
    for (const resolution of response.resolutions) {
      if (resolution.canonicalKey && isCanonicalKey(resolution.canonicalKey)) {
        resolutions.push({
          elementId: resolution.elementId,
          canonicalKey: resolution.canonicalKey,
          tier: 'model',
          confidence: resolution.confidence
        });
        newlyResolved[resolution.elementId] = resolution.canonicalKey;
      }
    }

    if (Object.keys(newlyResolved).length > 0) {
      const mergedMap: LearnedFieldMap = {
        host: message.host,
        formSignature: message.formSignature,
        elementToKey: { ...(learnedMap?.elementToKey ?? {}), ...newlyResolved },
        learnedAt: new Date().toISOString(),
        learnedAtEffort: message.effort,
        hitCount: (learnedMap?.hitCount ?? 0) + 1
      };
      await setCachedFieldMap(mergedMap);
      await persistLearnedFieldMap(settings.backendBaseUrl, mergedMap);
    }

    return {
      type: 'resolve-unresolved-fields-result',
      resolutions,
      backendUnavailable: false,
      modelFallbackDisabled: false
    };
  } catch {
    return {
      type: 'resolve-unresolved-fields-result',
      resolutions,
      backendUnavailable: true,
      modelFallbackDisabled: false
    };
  }
}

async function handleFetchDocument(message: FetchDocumentRequest): Promise<FetchDocumentResponse> {
  try {
    const response = await fetchWithTimeout(message.downloadUrl);
    if (!response.ok) {
      return {
        type: 'fetch-document-result',
        ok: false,
        error: `Download failed: ${response.status} ${response.statusText}`
      };
    }
    const blob = await response.blob();
    const buffer = await blob.arrayBuffer();
    return {
      type: 'fetch-document-result',
      ok: true,
      fileName: message.fileName,
      mimeType: blob.type || 'application/octet-stream',
      buffer
    };
  } catch (error) {
    return {
      type: 'fetch-document-result',
      ok: false,
      error: error instanceof Error ? error.message : 'Unknown download error'
    };
  }
}

async function handleGetConnectionStatus(): Promise<GetConnectionStatusResponse> {
  const settings = await getSettings();
  try {
    const response = await fetchWithTimeout(apiUrl(settings.backendBaseUrl, '/api/autofill/profile'));
    return {
      type: 'get-connection-status-result',
      reachable: response.ok,
      backendBaseUrl: settings.backendBaseUrl
    };
  } catch {
    return {
      type: 'get-connection-status-result',
      reachable: false,
      backendBaseUrl: settings.backendBaseUrl
    };
  }
}

async function dispatch(message: BackgroundRequest): Promise<BackgroundResponse> {
  switch (message.type) {
    case 'get-profile':
      return handleGetProfile();
    case 'resolve-unresolved-fields':
      return handleResolveUnresolvedFields(message);
    case 'fetch-document':
      return handleFetchDocument(message);
    case 'get-connection-status':
      return handleGetConnectionStatus();
    default: {
      const exhaustive: never = message;
      throw new Error(`Unhandled background message: ${JSON.stringify(exhaustive)}`);
    }
  }
}

chrome.runtime.onMessage.addListener((message: BackgroundRequest, _sender, sendResponse) => {
  dispatch(message)
    .then(sendResponse)
    .catch((error: unknown) => {
      sendResponse({
        type: 'fetch-document-result',
        ok: false,
        error: error instanceof Error ? error.message : 'Unknown background error'
      } satisfies FetchDocumentResponse);
    });
  return true; // keep the message channel open for the async response
});
