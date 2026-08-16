import type { ModelEffort } from '@/api/types';
import { EFFORT_LEVELS, formatTokenEstimate } from '@/lib/effort';
import { Badge } from '@/components/ui/Badge';
import { cn } from '@/lib/utils';

export interface EffortControlProps {
  value: ModelEffort;
  onChange: (value: ModelEffort) => void;
}

/**
 * A segmented control rather than a slider: there are exactly four named
 * levels, each with a fixed token estimate and a fixed set of ops it
 * unlocks (CONTRACTS.md §6), not a continuous range. A slider would imply
 * the user is picking an arbitrary point on a spectrum; these are four
 * discrete, deliberate presets, so four equally-visible buttons — the same
 * "selectable card" pattern already used for GitHub repo selection in
 * KnowledgeImport — read more honestly than a drag handle would.
 */
export function EffortControl({ value, onChange }: EffortControlProps) {
  return (
    <div className="flex flex-col gap-2">
      <div role="radiogroup" aria-label="Model effort" className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        {EFFORT_LEVELS.map((level) => {
          const selected = level.value === value;
          return (
            <button
              key={level.value}
              type="button"
              role="radio"
              aria-checked={selected}
              onClick={() => onChange(level.value)}
              className={cn(
                'flex flex-col items-start gap-1.5 rounded-[var(--radius-md)] border px-3 py-2.5 text-left transition-colors',
                'focus-visible:outline focus-visible:outline-2 focus-visible:outline-[var(--accent)] focus-visible:outline-offset-1',
                selected
                  ? 'border-[var(--accent)] bg-[var(--accent-soft)]'
                  : 'border-[var(--border)] bg-[var(--bg-elevated)] hover:bg-[var(--bg-inset)]',
              )}
            >
              <span className="flex w-full items-center justify-between gap-2">
                <span
                  className={cn('text-sm font-semibold', selected ? 'text-[var(--accent-text)]' : 'text-[var(--text)]')}
                >
                  {level.label}
                </span>
                <Badge tone={selected ? 'accent' : 'neutral'} className="font-mono">
                  {formatTokenEstimate(level.estimatedOutputTokens)}
                </Badge>
              </span>
              <span className="text-xs text-[var(--text-muted)]">{level.description}</span>
            </button>
          );
        })}
      </div>
      <p className="text-xs text-[var(--text-faint)]">
        Higher effort buys more rewriting of what your knowledge base already supports — never permission to invent
        a metric, employer, or date. The fabrication guard runs the same way at every level.
      </p>
    </div>
  );
}
