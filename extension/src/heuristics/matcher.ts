import { CANONICAL_KEYS, type CanonicalKey, type ModelEffort } from '../contracts';
import type { FieldDescriptor } from '../content/types';
import { AUTOCOMPLETE_MAP, SYNONYMS } from './synonyms';

/**
 * Confidence at or above which the heuristic tier accepts a match, per
 * effort level (CONTRACTS.md §10). Higher effort raises the bar, so the
 * free matcher keeps only what it's confident about and hands more of the
 * form to the (token-spending) model fallback.
 */
export const ACCEPT_THRESHOLD_BY_EFFORT: Record<ModelEffort, number> = {
  minimal: 0.6,
  standard: 0.72,
  thorough: 0.8,
  maximum: 0.88
};

/** The `standard`-effort threshold — kept as the module's historical default. */
export const ACCEPT_THRESHOLD = ACCEPT_THRESHOLD_BY_EFFORT.standard;

/**
 * Below this, a score is treated as noise and clamped to 0 rather than
 * reported as a weak positive. Chosen so that a single incidental word
 * shared between a field and a two-or-more-word synonym phrase (e.g. the
 * word "company" alone) never counts as a match — see matcher.test.ts.
 */
export const MIN_CONFIDENCE_FLOOR = 0.34;

const STOPWORDS = new Set([
  'a',
  'an',
  'the',
  'is',
  'are',
  'was',
  'were',
  'you',
  'your',
  'yours',
  'to',
  'of',
  'for',
  'in',
  'on',
  'at',
  'this',
  'that',
  'i',
  'we',
  'will',
  'do',
  'does',
  'did',
  'please',
  'enter',
  'select',
  'choose',
  'provide',
  'and',
  'or',
  'be',
  'if',
  'my',
  'our'
]);

/** Splits camelCase/snake_case/kebab-case/punctuated text into lowercase word tokens. */
export function tokenize(text: string): string[] {
  const spaced = text
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[^a-zA-Z0-9]+/g, ' ')
    .toLowerCase();
  return spaced
    .split(/\s+/)
    .map((t) => t.trim())
    .filter((t) => t.length > 0 && !STOPWORDS.has(t));
}

function fullPhraseMatchScore(sourceTokens: string[], phrase: string): number {
  if (sourceTokens.length === 0) return 0;
  const phraseTokens = tokenize(phrase);
  if (phraseTokens.length === 0) return 0;
  const sourceSet = new Set(sourceTokens);
  const contained = phraseTokens.every((t) => sourceSet.has(t));
  if (!contained) return 0;
  // Reward phrases that explain most of the source text; a phrase fully
  // contained in a short, closely-matching source scores near 1.0, while the
  // same phrase buried in a much longer, noisier source scores lower.
  return phraseTokens.length / sourceTokens.length;
}

function normalizedAutocompleteToken(autoComplete: string): string {
  const parts = autoComplete.trim().toLowerCase().split(/\s+/);
  return parts.length > 0 ? (parts[parts.length - 1] ?? '') : '';
}

/**
 * Scores one field descriptor against one canonical key. Pure and
 * side-effect free. An exact `autocomplete` token match short-circuits to
 * 1.0; otherwise the best full-phrase-containment score across every
 * label/name/id/placeholder/aria-label source and every synonym phrase for
 * `key` wins, clamped to 0 below `MIN_CONFIDENCE_FLOOR`.
 */
export function scoreField(descriptor: FieldDescriptor, key: CanonicalKey): number {
  if (descriptor.autoComplete) {
    const token = normalizedAutocompleteToken(descriptor.autoComplete);
    if (token && AUTOCOMPLETE_MAP[token] === key) {
      return 1;
    }
  }

  const sources = [
    descriptor.label,
    descriptor.name,
    descriptor.id,
    descriptor.placeholder,
    descriptor.ariaLabel
  ].filter((s): s is string => typeof s === 'string' && s.trim().length > 0);

  const phrases = SYNONYMS[key];
  let best = 0;
  for (const source of sources) {
    const sourceTokens = tokenize(source);
    for (const phrase of phrases) {
      const score = fullPhraseMatchScore(sourceTokens, phrase);
      if (score > best) best = score;
    }
  }

  return best < MIN_CONFIDENCE_FLOOR ? 0 : best;
}

export interface HeuristicMatch {
  key: CanonicalKey;
  confidence: number;
}

/**
 * Resolves every descriptor against every canonical key, keeping only the
 * best-scoring key per field and only when it clears the accept threshold
 * for `effort` (CONTRACTS.md §10; defaults to `standard`, i.e.
 * `ACCEPT_THRESHOLD`). This is tier 2 of the cascade — zero model tokens,
 * regardless of effort; effort only shifts how much of the form tier 2 is
 * willing to keep for itself versus leaving for the model fallback.
 */
export function resolveByHeuristic(
  descriptors: readonly FieldDescriptor[],
  effort: ModelEffort = 'standard'
): Map<string, HeuristicMatch> {
  const result = new Map<string, HeuristicMatch>();
  const acceptThreshold = ACCEPT_THRESHOLD_BY_EFFORT[effort];

  for (const descriptor of descriptors) {
    let bestKey: CanonicalKey | undefined;
    let bestScore = 0;

    for (const key of CANONICAL_KEYS) {
      const score = scoreField(descriptor, key);
      if (score > bestScore) {
        bestScore = score;
        bestKey = key;
      }
    }

    if (bestKey && bestScore >= acceptThreshold) {
      result.set(descriptor.elementId, { key: bestKey, confidence: bestScore });
    }
  }

  return result;
}
