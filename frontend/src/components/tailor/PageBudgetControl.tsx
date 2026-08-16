import { cn } from '@/lib/utils';

export type PageBudget = 1 | 2;

export interface PageBudgetControlProps {
  value: PageBudget;
  onChange: (value: PageBudget) => void;
}

interface PageLimitInfo {
  value: PageBudget;
  label: string;
  description: string;
}

const PAGE_LIMITS: readonly PageLimitInfo[] = [
  {
    value: 1,
    label: '1 page',
    description: 'Cuts to the highest-scoring role; certifications and projects go first.',
  },
  {
    value: 2,
    label: '2 pages',
    description: 'The contract default — room for a fuller history.',
  },
];

/**
 * A segmented control rather than a stepper: the backend enforces exactly
 * two page budgets deterministically (CONTRACTS.md §6 "Page budget"), not an
 * arbitrary page count. Two equally-visible buttons — the same "selectable
 * card" pattern EffortControl already uses for model effort — read more
 * honestly than a numeric input would.
 */
export function PageBudgetControl({ value, onChange }: PageBudgetControlProps) {
  return (
    <div className="flex flex-col gap-2">
      <div role="radiogroup" aria-label="Page limit" className="grid grid-cols-2 gap-2">
        {PAGE_LIMITS.map((limit) => {
          const selected = limit.value === value;
          return (
            <button
              key={limit.value}
              type="button"
              role="radio"
              aria-checked={selected}
              onClick={() => onChange(limit.value)}
              className={cn(
                'flex flex-col items-start gap-1.5 rounded-[var(--radius-md)] border px-3 py-2.5 text-left transition-colors',
                'focus-visible:outline focus-visible:outline-2 focus-visible:outline-[var(--accent)] focus-visible:outline-offset-1',
                selected
                  ? 'border-[var(--accent)] bg-[var(--accent-soft)]'
                  : 'border-[var(--border)] bg-[var(--bg-elevated)] hover:bg-[var(--bg-inset)]',
              )}
            >
              <span
                className={cn('text-sm font-semibold', selected ? 'text-[var(--accent-text)]' : 'text-[var(--text)]')}
              >
                {limit.label}
              </span>
              <span className="text-xs text-[var(--text-muted)]">{limit.description}</span>
            </button>
          );
        })}
      </div>
      <p className="text-xs text-[var(--text-faint)]">
        The single highest-scoring experience entry is never cut to make the budget. A resume that still doesn't
        fit at the floor renders over budget rather than losing substance, and the diff says so.
      </p>
    </div>
  );
}
