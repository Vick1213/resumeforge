import { describe, expect, it } from 'vitest';
import { ACCEPT_THRESHOLD, MIN_CONFIDENCE_FLOOR, scoreField } from '../src/heuristics/matcher';
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
