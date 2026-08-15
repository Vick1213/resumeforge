import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { EmptyState } from '@/components/ui/EmptyState';

describe('EmptyState', () => {
  it('renders the title, description, and provided action', () => {
    render(
      <EmptyState
        title="Nothing here yet"
        description="Add your first item to get started."
        action={<button type="button">Add item</button>}
      />,
    );

    expect(screen.getByText('Nothing here yet')).toBeInTheDocument();
    expect(screen.getByText('Add your first item to get started.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add item' })).toBeInTheDocument();
  });
});
