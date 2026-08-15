import { describe, expect, it } from 'vitest';
import { fuzzyMatchOption } from '../src/content/fill';

describe('fuzzyMatchOption', () => {
  it('matches an exact option case-insensitively', () => {
    expect(fuzzyMatchOption(['Yes', 'No'], 'yes')).toBe('Yes');
  });

  it('matches via substring containment when phrasing is a superset', () => {
    expect(fuzzyMatchOption(['United States of America', 'Canada'], 'United States')).toBe(
      'United States of America'
    );
  });

  it('matches via token overlap when neither string contains the other', () => {
    const match = fuzzyMatchOption(["Bachelor's Degree in Science", 'Master of Arts'], 'Bachelor of Science');
    expect(match).toBe("Bachelor's Degree in Science");
  });

  it('returns null instead of guessing when nothing is a confident match', () => {
    expect(fuzzyMatchOption(['Engineering', 'Sales', 'Marketing'], 'Human Resources')).toBeNull();
  });
});
