import { describe, expect, it } from 'vitest';
import { groupDiffByKind } from '@/lib/diffGrouping';
import type { ResumeDiffEntry } from '@/api/types';

describe('groupDiffByKind', () => {
  it('groups entries by kind, orders prose changes first, and omits kinds with no entries', () => {
    const diff: ResumeDiffEntry[] = [
      { entityId: 'exp:a#0', kind: 'excluded' },
      { entityId: 'exp:a#1', kind: 'rewritten', before: 'x', after: 'y' },
      { entityId: 'exp:a#2', kind: 'rewritten', before: 'p', after: 'q' },
      { entityId: 'sum', kind: 'summarySet', after: 'new summary' },
    ];

    const groups = groupDiffByKind(diff);

    expect(groups.map((group) => group.kind)).toEqual(['rewritten', 'excluded', 'summarySet']);
    expect(groups[0]?.entries).toHaveLength(2);
    expect(groups.some((group) => group.kind === 'reordered')).toBe(false);
  });
});
