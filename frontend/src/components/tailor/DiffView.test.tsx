import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { DiffView } from '@/components/tailor/DiffView';
import type { ResumeDiffEntry } from '@/api/types';

describe('DiffView', () => {
  it('renders a keywordsInjected entry with the "Keywords woven in" badge and the keywords visible', () => {
    const diff: ResumeDiffEntry[] = [
      {
        entityId: 'exp:acme-corp#1',
        kind: 'keywordsInjected',
        before: 'Led migration of 40+ services from .NET 6 to .NET 8.',
        after: 'Led migration of 40+ services on a Kubernetes-based distributed systems platform.',
        keywords: ['kubernetes', 'distributed systems'],
      },
    ];

    render(<DiffView diff={diff} />);

    expect(screen.getByText('Keywords woven in')).toBeInTheDocument();
    expect(screen.getByText('kubernetes')).toBeInTheDocument();
    expect(screen.getByText('distributed systems')).toBeInTheDocument();
  });

  it('renders a plain rewrite without the keyword badge', () => {
    const diff: ResumeDiffEntry[] = [
      { entityId: 'exp:acme-corp#0', kind: 'rewritten', before: 'Old text.', after: 'New text.' },
    ];

    render(<DiffView diff={diff} />);

    expect(screen.queryByText('Keywords woven in')).not.toBeInTheDocument();
  });
});
