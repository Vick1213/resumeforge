import type { AutofillProfile, CanonicalKey, ResolutionTier, UnresolvedField } from './contracts';

/**
 * Runtime message protocol between the content script, the popup, and the
 * background service worker. The service worker is the only piece that
 * ever talks to the backend (per CONTRACTS.md §10's design and the
 * project's cost story) — every message here that crosses into "does this
 * need the network" territory is answered by `background/service-worker.ts`.
 */

export interface FieldTierResolution {
  elementId: string;
  canonicalKey: CanonicalKey | '';
  tier: ResolutionTier;
  confidence: number;
}

export interface ResolveUnresolvedFieldsRequest {
  type: 'resolve-unresolved-fields';
  host: string;
  formSignature: string;
  fields: UnresolvedField[];
}

export interface ResolveUnresolvedFieldsResponse {
  type: 'resolve-unresolved-fields-result';
  resolutions: FieldTierResolution[];
  backendUnavailable: boolean;
  modelFallbackDisabled: boolean;
}

export interface GetProfileRequest {
  type: 'get-profile';
}

export interface GetProfileResponse {
  type: 'get-profile-result';
  profile: AutofillProfile | null;
  fromCache: boolean;
  backendUnavailable: boolean;
}

export interface FetchDocumentRequest {
  type: 'fetch-document';
  downloadUrl: string;
  fileName: string;
}

export interface FetchDocumentResponse {
  type: 'fetch-document-result';
  ok: boolean;
  fileName?: string;
  mimeType?: string;
  buffer?: ArrayBuffer;
  error?: string;
}

export interface GetConnectionStatusRequest {
  type: 'get-connection-status';
}

export interface GetConnectionStatusResponse {
  type: 'get-connection-status-result';
  reachable: boolean;
  backendBaseUrl: string;
}

export type BackgroundRequest =
  | ResolveUnresolvedFieldsRequest
  | GetProfileRequest
  | FetchDocumentRequest
  | GetConnectionStatusRequest;

export type BackgroundResponse =
  | ResolveUnresolvedFieldsResponse
  | GetProfileResponse
  | FetchDocumentResponse
  | GetConnectionStatusResponse;

/** Popup -> content script: run the full scan/resolve/preview cascade. */
export interface RunAutofillRequest {
  type: 'run-autofill';
}

export interface RunAutofillResponse {
  type: 'run-autofill-result';
  ok: boolean;
  adapterId: string | null;
  tierCounts: Record<ResolutionTier, number>;
  fieldCount: number;
  error?: string;
}

export type ContentRequest = RunAutofillRequest;
export type ContentResponse = RunAutofillResponse;
