import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AtsReviewPanel } from './AtsReviewPanel';
import type { AtsReview } from '@/api/types';

const review: AtsReview = {
  scoreBefore: 61,
  scoreAfter: 84,
  verdict: 'Two of the posting’s core terms never appear outside the skills list.',
  gaps: [
    {
      keyword: 'Kubernetes',
      importance: 'critical',
      skillsOnly: true,
      placement: 'exp:nimbus#1',
      angle: 'The migration across 40+ services ran on Kubernetes; name it in that bullet.',
    },
    {
      keyword: 'Terraform',
      importance: 'niceToHave',
      skillsOnly: false,
      placement: null,
      angle: 'Nothing in the resume evidences infrastructure-as-code.',
    },
  ],
  recruiterNotes: ['Kubernetes appears only in the skills list.'],
};

describe('AtsReviewPanel', () => {
  it('shows both scores and the headroom between them', () => {
    render(<AtsReviewPanel review={review} />);

    expect(screen.getByLabelText('ATS score now: 61 out of 100')).toBeInTheDocument();
    expect(screen.getByLabelText('ATS score after fixes: 84 out of 100')).toBeInTheDocument();
    expect(screen.getByText('+23 points available')).toBeInTheDocument();
  });

  it('marks a gap that the skills list already covers, since only a bullet can close it', () => {
    render(<AtsReviewPanel review={review} />);

    expect(screen.getByText('Listed in skills, never evidenced')).toBeInTheDocument();
    expect(screen.getByText('exp:nimbus#1')).toBeInTheDocument();
  });

  it('renders every gap with the angle it should be evidencing', () => {
    render(<AtsReviewPanel review={review} />);

    expect(screen.getByText('Kubernetes')).toBeInTheDocument();
    expect(screen.getByText('Terraform')).toBeInTheDocument();
    expect(screen.getByText(/name it in that bullet/)).toBeInTheDocument();
  });
});
