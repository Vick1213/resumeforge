import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { CoveragePanel } from '@/components/tailor/CoveragePanel';
import type { CoverageReport } from '@/api/types';

const coverage: CoverageReport = {
  score: 0.5,
  requirements: [
    { requirementId: 'req:0', requirementText: 'Requirement A', covered: true, evidenceIds: ['exp:a#0'] },
    { requirementId: 'req:1', requirementText: 'Requirement B', covered: false, evidenceIds: [] },
  ],
};

describe('CoveragePanel', () => {
  it('renders the overall score and each requirement with its evidence', () => {
    render(<CoveragePanel coverage={coverage} />);

    expect(screen.getByText('50%')).toBeInTheDocument();
    expect(screen.getByText('Requirement A')).toBeInTheDocument();
    expect(screen.getByText('Requirement B')).toBeInTheDocument();
    expect(screen.getByText('exp:a#0')).toBeInTheDocument();
    expect(screen.getByText('No evidence found')).toBeInTheDocument();
  });
});
