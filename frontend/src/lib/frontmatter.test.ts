import { describe, expect, it } from 'vitest';
import { parseBullets, splitMarkdown, validateFrontmatter } from '@/lib/frontmatter';

describe('splitMarkdown + parseBullets', () => {
  it('splits frontmatter from the body and parses wrapped bullets with indented variants', () => {
    const raw = [
      '---',
      'type: experience',
      'role: Engineer',
      '---',
      '',
      '- Cut latency from 840ms to 120ms by rebuilding the fan-out',
      '  across six services.',
      '  - Short variant of the same bullet.',
      '- Second independent bullet.',
    ].join('\n');

    const { frontmatter, body, error } = splitMarkdown(raw);
    expect(error).toBeUndefined();
    expect(frontmatter['type']).toBe('experience');
    expect(frontmatter['role']).toBe('Engineer');

    const bullets = parseBullets(body);
    expect(bullets).toHaveLength(2);
    expect(bullets[0]?.text).toBe('Cut latency from 840ms to 120ms by rebuilding the fan-out across six services.');
    expect(bullets[0]?.variants).toEqual(['Short variant of the same bullet.']);
    expect(bullets[1]?.text).toBe('Second independent bullet.');
  });
});

describe('validateFrontmatter', () => {
  it('enforces per-type required fields and rejects unknown types', () => {
    const missing = validateFrontmatter({ type: 'experience' });
    expect(missing.isValid).toBe(false);
    expect(missing.issues.map((issue) => issue.field).sort()).toEqual(['organization', 'role', 'startDate']);

    const valid = validateFrontmatter({
      type: 'experience',
      role: 'Senior Engineer',
      organization: 'Acme Corp',
      startDate: '2022-03',
    });
    expect(valid.isValid).toBe(true);
    expect(valid.issues).toHaveLength(0);

    const unknownType = validateFrontmatter({ type: 'hobby' });
    expect(unknownType.isValid).toBe(false);
  });
});
