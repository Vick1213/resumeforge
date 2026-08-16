import { useMemo } from 'react';
import type { KnowledgeItemDto } from '@/api/types';
import { Button } from '@/components/ui/Button';
import { cn } from '@/lib/utils';

export type ProjectSelectionState = 'auto' | 'always' | 'never';

export interface ProjectSelectionPanelProps {
  /** All knowledge items; filtered down to `kind === 'project'` internally. */
  items: KnowledgeItemDto[];
  /** Keyed by knowledge-item id; a missing entry means 'auto'. */
  value: Record<string, ProjectSelectionState>;
  onChange: (itemId: string, state: ProjectSelectionState) => void;
  onReset: () => void;
}

interface SelectionOption {
  value: ProjectSelectionState;
  label: string;
}

const OPTIONS: readonly SelectionOption[] = [
  { value: 'auto', label: 'Auto' },
  { value: 'always', label: 'Always' },
  { value: 'never', label: 'Never' },
];

/**
 * Per-project override for automatic relevance selection (CONTRACTS.md §12):
 * "always" rows become `TailorRequest.pinnedEntryIds`, "never" rows become
 * `excludedEntryIds`. One compact radiogroup per row rather than a single
 * global control, because the decision is per-project — the same
 * "selectable card" idiom as EffortControl/PageBudgetControl, shrunk down so
 * many rows still read at a glance.
 */
export function ProjectSelectionPanel({ items, value, onChange, onReset }: ProjectSelectionPanelProps) {
  const projects = useMemo(
    () =>
      items
        .filter((item) => item.kind === 'project')
        .slice()
        .sort((a, b) => a.title.localeCompare(b.title)),
    [items],
  );

  const alwaysCount = projects.filter((item) => (value[item.id] ?? 'auto') === 'always').length;
  const neverCount = projects.filter((item) => (value[item.id] ?? 'auto') === 'never').length;
  const summaryParts = [
    alwaysCount > 0 ? `${alwaysCount} always` : undefined,
    neverCount > 0 ? `${neverCount} never` : undefined,
  ].filter((part): part is string => part !== undefined);

  if (projects.length === 0) {
    return <p className="text-xs text-[var(--text-faint)]">No projects in your knowledge base yet.</p>;
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-col gap-1.5">
        {projects.map((item) => {
          const state = value[item.id] ?? 'auto';
          return (
            <div
              key={item.id}
              className="flex items-center justify-between gap-3 rounded-[var(--radius-md)] border border-[var(--border)] bg-[var(--bg-elevated)] px-3 py-2"
            >
              <span className="truncate text-sm text-[var(--text)]">{item.title}</span>
              <div role="radiogroup" aria-label={item.title} className="flex shrink-0 gap-1">
                {OPTIONS.map((option) => {
                  const selected = option.value === state;
                  return (
                    <button
                      key={option.value}
                      type="button"
                      role="radio"
                      aria-checked={selected}
                      onClick={() => onChange(item.id, option.value)}
                      className={cn(
                        'rounded-[var(--radius-sm)] px-2 py-1 text-xs font-medium transition-colors',
                        'focus-visible:outline focus-visible:outline-2 focus-visible:outline-[var(--accent)] focus-visible:outline-offset-1',
                        selected
                          ? 'bg-[var(--accent)] text-white'
                          : 'text-[var(--text-muted)] hover:bg-[var(--bg-inset)]',
                      )}
                    >
                      {option.label}
                    </button>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>
      {summaryParts.length > 0 && (
        <div className="flex items-center justify-between gap-3">
          <p className="text-xs text-[var(--text-faint)]">{summaryParts.join(' · ')}</p>
          <Button variant="ghost" size="sm" onClick={onReset}>
            Reset
          </Button>
        </div>
      )}
    </div>
  );
}
