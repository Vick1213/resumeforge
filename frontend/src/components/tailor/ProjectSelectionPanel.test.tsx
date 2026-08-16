import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { ProjectSelectionPanel } from '@/components/tailor/ProjectSelectionPanel';
import type { ProjectSelectionState } from '@/components/tailor/ProjectSelectionPanel';
import type { KnowledgeItemDto } from '@/api/types';

function knowledgeItem(overrides: Partial<KnowledgeItemDto>): KnowledgeItemDto {
  return {
    id: 'prj:placeholder',
    kind: 'project',
    title: 'Placeholder Project',
    tech: [],
    tags: [],
    source: 'manual',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

const ITEMS: KnowledgeItemDto[] = [
  knowledgeItem({ id: 'prj:market-terminal', title: 'Market Terminal' }),
  knowledgeItem({ id: 'prj:graph-runner', title: 'Graph Runner' }),
  knowledgeItem({ id: 'exp:acme-corp', kind: 'experience', title: 'Senior Software Engineer' }),
];

describe('ProjectSelectionPanel', () => {
  it('renders one row per project, sorted by title, defaulting every radiogroup to Auto', () => {
    render(<ProjectSelectionPanel items={ITEMS} value={{}} onChange={() => {}} onReset={() => {}} />);

    const groups = screen.getAllByRole('radiogroup');
    expect(groups).toHaveLength(2);
    expect(groups[0]).toHaveAccessibleName('Graph Runner');
    expect(groups[1]).toHaveAccessibleName('Market Terminal');

    for (const group of groups) {
      expect(within(group).getByRole('radio', { name: 'Auto' })).toHaveAttribute('aria-checked', 'true');
      expect(within(group).getByRole('radio', { name: 'Always' })).toHaveAttribute('aria-checked', 'false');
      expect(within(group).getByRole('radio', { name: 'Never' })).toHaveAttribute('aria-checked', 'false');
    }
  });

  it('does not render a row for non-project knowledge items', () => {
    render(<ProjectSelectionPanel items={ITEMS} value={{}} onChange={() => {}} onReset={() => {}} />);

    expect(screen.queryByText('Senior Software Engineer')).not.toBeInTheDocument();
  });

  it('fires onChange with the item id and the chosen state when Always is picked', () => {
    const onChange = vi.fn();
    render(<ProjectSelectionPanel items={ITEMS} value={{}} onChange={onChange} onReset={() => {}} />);

    const group = screen.getByRole('radiogroup', { name: 'Market Terminal' });
    fireEvent.click(within(group).getByRole('radio', { name: 'Always' }));

    expect(onChange).toHaveBeenCalledWith('prj:market-terminal', 'always');
  });

  it('fires onChange with the item id and the chosen state when Never is picked', () => {
    const onChange = vi.fn();
    render(<ProjectSelectionPanel items={ITEMS} value={{}} onChange={onChange} onReset={() => {}} />);

    const group = screen.getByRole('radiogroup', { name: 'Graph Runner' });
    fireEvent.click(within(group).getByRole('radio', { name: 'Never' }));

    expect(onChange).toHaveBeenCalledWith('prj:graph-runner', 'never');
  });

  it('shows no summary line and no reset affordance when every row is Auto', () => {
    render(<ProjectSelectionPanel items={ITEMS} value={{}} onChange={() => {}} onReset={() => {}} />);

    expect(screen.queryByText(/always/)).not.toBeInTheDocument();
    expect(screen.queryByText(/never/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reset' })).not.toBeInTheDocument();
  });

  it('summarizes non-Auto rows as counts of always and never', () => {
    const value: Record<string, ProjectSelectionState> = {
      'prj:market-terminal': 'always',
      'prj:graph-runner': 'never',
    };
    render(<ProjectSelectionPanel items={ITEMS} value={value} onChange={() => {}} onReset={() => {}} />);

    expect(screen.getByText('1 always · 1 never')).toBeInTheDocument();
  });

  it('calls onReset when the Reset affordance is clicked', () => {
    const onReset = vi.fn();
    const value: Record<string, ProjectSelectionState> = { 'prj:market-terminal': 'always' };
    render(<ProjectSelectionPanel items={ITEMS} value={value} onChange={() => {}} onReset={onReset} />);

    fireEvent.click(screen.getByRole('button', { name: 'Reset' }));

    expect(onReset).toHaveBeenCalledTimes(1);
  });
});
