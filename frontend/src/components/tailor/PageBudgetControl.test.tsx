import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { PageBudgetControl } from '@/components/tailor/PageBudgetControl';

describe('PageBudgetControl', () => {
  it('defaults to 2 pages, marking it as the selected radio', () => {
    render(<PageBudgetControl value={2} onChange={() => {}} />);

    expect(screen.getByRole('radio', { name: /2 pages/ })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radio', { name: /1 page/ })).toHaveAttribute('aria-checked', 'false');
  });

  it('reports the correct numeric value when a different limit is chosen', () => {
    const onChange = vi.fn();
    render(<PageBudgetControl value={2} onChange={onChange} />);

    fireEvent.click(screen.getByRole('radio', { name: /1 page/ }));

    expect(onChange).toHaveBeenCalledWith(1);
  });

  it('exposes both options within an accessible radiogroup', () => {
    render(<PageBudgetControl value={1} onChange={() => {}} />);

    expect(screen.getByRole('radiogroup', { name: 'Page limit' })).toBeInTheDocument();
    expect(screen.getAllByRole('radio')).toHaveLength(2);
  });
});
