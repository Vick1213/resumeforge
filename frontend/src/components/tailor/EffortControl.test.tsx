import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { EffortControl } from '@/components/tailor/EffortControl';
import { DEFAULT_EFFORT } from '@/lib/effort';

describe('EffortControl', () => {
  it('defaults to standard, marking it as the selected radio', () => {
    render(<EffortControl value={DEFAULT_EFFORT} onChange={() => {}} />);

    expect(DEFAULT_EFFORT).toBe('standard');
    expect(screen.getByRole('radio', { name: /Standard/ })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radio', { name: /Minimal/ })).toHaveAttribute('aria-checked', 'false');
  });

  it('reports the correct lowercase wire value when a different level is chosen', () => {
    const onChange = vi.fn();
    render(<EffortControl value="standard" onChange={onChange} />);

    fireEvent.click(screen.getByRole('radio', { name: /Maximum/ }));

    expect(onChange).toHaveBeenCalledWith('maximum');
  });

  it('shows the estimated output-token cost beside every level', () => {
    render(<EffortControl value="standard" onChange={() => {}} />);

    expect(screen.getByText('~200 tokens')).toBeInTheDocument();
    expect(screen.getByText('~600 tokens')).toBeInTheDocument();
    expect(screen.getByText('~1,200 tokens')).toBeInTheDocument();
    expect(screen.getByText('~2,000 tokens')).toBeInTheDocument();
  });
});
