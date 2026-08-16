import { describe, expect, it } from 'vitest';
import {
  ACCEPT_THRESHOLD,
  ACCEPT_THRESHOLD_BY_EFFORT,
  MIN_CONFIDENCE_FLOOR,
  resolveByHeuristic,
  scoreField
} from '../src/heuristics/matcher';
import type { FieldDescriptor } from '../src/content/types';

function descriptor(overrides: Partial<FieldDescriptor>): FieldDescriptor {
  return {
    elementId: 'rf-1',
    label: null,
    name: null,
    id: null,
    placeholder: null,
    ariaLabel: null,
    autoComplete: null,
    inputType: 'text',
    options: [],
    ...overrides
  };
}

describe('scoreField', () => {
  it('accepts an exact label match at or above the accept threshold', () => {
    const field = descriptor({ label: 'Email Address' });
    expect(scoreField(field, 'email')).toBeGreaterThanOrEqual(ACCEPT_THRESHOLD);
  });

  it('short-circuits to 1.0 on an exact autocomplete token match, per the WHATWG map', () => {
    const field = descriptor({ label: 'Some oddly worded label', autoComplete: 'given-name' });
    expect(scoreField(field, 'firstName')).toBe(1);
  });

  it('does not let an autocomplete token inflate the score of an unrelated key', () => {
    const field = descriptor({ label: 'Some oddly worded label', autoComplete: 'given-name' });
    expect(scoreField(field, 'lastName')).toBeLessThan(ACCEPT_THRESHOLD);
  });

  it('does not match "Company you are applying to" to currentCompany more than it matches nothing', () => {
    const field = descriptor({ label: 'Company you are applying to' });
    const score = scoreField(field, 'currentCompany');
    expect(score).toBe(0);
    expect(score).toBeLessThan(MIN_CONFIDENCE_FLOOR);
  });

  it('does not let one incidental shared word cross the accept threshold', () => {
    // Shares only "title" with currentTitle's synonyms — must stay unresolved
    // rather than false-positive on a single common word.
    const field = descriptor({ label: 'Title of your favorite book' });
    expect(scoreField(field, 'currentTitle')).toBeLessThan(ACCEPT_THRESHOLD);
  });
});

describe('ACCEPT_THRESHOLD_BY_EFFORT', () => {
  it('matches the tier-2 accept threshold table in CONTRACTS.md §10', () => {
    expect(ACCEPT_THRESHOLD_BY_EFFORT).toEqual({
      minimal: 0.6,
      standard: 0.72,
      thorough: 0.8,
      maximum: 0.88
    });
  });

  it('keeps ACCEPT_THRESHOLD equal to the standard-effort threshold, for backward compatibility', () => {
    expect(ACCEPT_THRESHOLD).toBe(ACCEPT_THRESHOLD_BY_EFFORT.standard);
  });
});

describe('resolveByHeuristic effort scaling', () => {
  // Tokenizes to ['notice', 'period', 'here'] once stopwords are stripped;
  // "notice period" (2 of 3 tokens) scores 0.667 — between the minimal
  // (0.60) and standard (0.72) thresholds, so this field is exactly the
  // borderline case the effort table is meant to move.
  const borderlineField: FieldDescriptor = descriptor({ label: 'Please provide your notice period here' });

  it('defaults to the standard threshold and leaves a borderline field unresolved', () => {
    const result = resolveByHeuristic([borderlineField]);
    expect(result.has(borderlineField.elementId)).toBe(false);
  });

  it('resolves the same borderline field at minimal effort, where the bar is lower', () => {
    const result = resolveByHeuristic([borderlineField], 'minimal');
    expect(result.get(borderlineField.elementId)?.key).toBe('noticePeriod');
  });

  it('still leaves it unresolved at thorough and maximum effort, where the bar is higher', () => {
    expect(resolveByHeuristic([borderlineField], 'thorough').has(borderlineField.elementId)).toBe(false);
    expect(resolveByHeuristic([borderlineField], 'maximum').has(borderlineField.elementId)).toBe(false);
  });
});
