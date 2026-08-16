/**
 * TypeScript mirror of docs/CONTRACTS.md §10 ("Autofill contracts").
 *
 * This file must stay in lockstep with the C# types in
 * `ResumeForge.Application.Autofill`. Field names use the wire's camelCase
 * form (the API serializes with `JsonNamingPolicy.CamelCase`), so these
 * interfaces can be used directly against `fetch` responses with no mapping
 * layer. If you change a shape here, change docs/CONTRACTS.md §10 first.
 */

/**
 * The closed set of canonical field keys the backend and extension agree on.
 * Exactly the list in CONTRACTS.md §10 — do not add, remove, or rename a key
 * here without updating the backend and the doc in the same change.
 */
export type CanonicalKey =
  | 'firstName'
  | 'lastName'
  | 'fullName'
  | 'preferredName'
  | 'email'
  | 'phone'
  | 'addressLine1'
  | 'addressLine2'
  | 'city'
  | 'state'
  | 'postalCode'
  | 'country'
  | 'linkedin'
  | 'github'
  | 'portfolio'
  | 'website'
  | 'currentCompany'
  | 'currentTitle'
  | 'yearsExperience'
  | 'workAuthorization'
  | 'requiresSponsorship'
  | 'willingToRelocate'
  | 'noticePeriod'
  | 'desiredSalary'
  | 'availableStartDate'
  | 'gender'
  | 'ethnicity'
  | 'veteranStatus'
  | 'disabilityStatus'
  | 'howDidYouHear'
  | 'referredBy';

/** The full ordered list of `CanonicalKey`, for iteration and validation. */
export const CANONICAL_KEYS: readonly CanonicalKey[] = [
  'firstName',
  'lastName',
  'fullName',
  'preferredName',
  'email',
  'phone',
  'addressLine1',
  'addressLine2',
  'city',
  'state',
  'postalCode',
  'country',
  'linkedin',
  'github',
  'portfolio',
  'website',
  'currentCompany',
  'currentTitle',
  'yearsExperience',
  'workAuthorization',
  'requiresSponsorship',
  'willingToRelocate',
  'noticePeriod',
  'desiredSalary',
  'availableStartDate',
  'gender',
  'ethnicity',
  'veteranStatus',
  'disabilityStatus',
  'howDidYouHear',
  'referredBy'
];

export function isCanonicalKey(value: string): value is CanonicalKey {
  return (CANONICAL_KEYS as readonly string[]).includes(value);
}

/**
 * Mirrors `ModelEffort` (CONTRACTS.md §6). Buys more decisions from the
 * tailoring model and, per §10, raises the tier-2 heuristic's accept
 * threshold so more of the form is deliberately handed to the model.
 * Serializes as the lowercase name; `standard` is the wire default.
 */
export type ModelEffort = 'minimal' | 'standard' | 'thorough' | 'maximum';

/** Mirrors `AutofillProfile` — the user's fill-in values plus documents. */
export interface AutofillProfile {
  /** canonical key -> value. A key absent or null means "no value on file". */
  fields: Partial<Record<CanonicalKey, string | null>>;
  documents: AutofillDocument[];
}

/** Mirrors `AutofillDocument`. */
export interface AutofillDocument {
  kind: 'resume' | 'coverLetter';
  fileName: string;
  downloadUrl: string;
}

/** Mirrors `ResolveFieldsRequest` — the tier-3 model fallback request body. */
export interface ResolveFieldsRequest {
  host: string;
  formSignature: string;
  fields: UnresolvedField[];
  /** Defaults to 'standard' server-side when omitted. */
  effort?: ModelEffort;
}

/** Input control kinds the backend needs to reason about for resolution. */
export type AutofillInputType =
  | 'text'
  | 'email'
  | 'tel'
  | 'select'
  | 'radio'
  | 'checkbox'
  | 'file'
  | 'textarea';

/** Mirrors `UnresolvedField`. */
export interface UnresolvedField {
  elementId: string;
  label?: string | null;
  name?: string | null;
  placeholder?: string | null;
  autoComplete?: string | null;
  inputType: AutofillInputType;
  options: string[];
}

/** Mirrors `ResolveFieldsResponse`. */
export interface ResolveFieldsResponse {
  resolutions: FieldResolution[];
  usage: TokenUsage;
}

/** Mirrors `FieldResolution`. `canonicalKey` is `""` when genuinely unmappable. */
export interface FieldResolution {
  elementId: string;
  canonicalKey: CanonicalKey | '';
  confidence: number;
  optionValue?: string | null;
}

/** Mirrors `LearnedFieldMap` — the persisted, zero-token-next-time record. */
export interface LearnedFieldMap {
  host: string;
  formSignature: string;
  elementToKey: Record<string, string>;
  learnedAt: string;
  /**
   * The effort the map was learned at. A map learned at a lower effort is
   * still a valid cache hit, but re-running at higher effort must be able to
   * resolve fields the earlier pass left unmapped rather than treating the
   * cached map as complete (CONTRACTS.md §10). Defaults to 'standard'.
   */
  learnedAtEffort?: ModelEffort;
  hitCount: number;
}

/** Mirrors `TokenUsage` (§6), reused verbatim by the autofill endpoints. */
export interface TokenUsage {
  inputTokens: number;
  outputTokens: number;
  modelCalls: number;
  cacheHits: number;
}

/**
 * The tier that resolved a field. Not part of the wire contract — this is an
 * extension-local concept used to make the cascade observable in the overlay.
 */
export type ResolutionTier = 'adapter' | 'heuristic' | 'learned' | 'model' | 'unresolved';
