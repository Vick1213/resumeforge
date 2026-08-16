import type { BadgeTone } from '@/components/ui/Badge';
import { Badge } from '@/components/ui/Badge';
import type { DiffKind, ResumeDiffEntry } from '@/api/types';
import { groupDiffByKind } from '@/lib/diffGrouping';

const KIND_TONE: Record<DiffKind, BadgeTone> = {
  included: 'success',
  excluded: 'neutral',
  reordered: 'info',
  rewritten: 'accent',
  variantSelected: 'accent',
  summarySet: 'accent',
  skillEmphasized: 'info',
  keywordsInjected: 'info',
};

export interface DiffViewProps {
  diff: ResumeDiffEntry[];
}

export function DiffView({ diff }: DiffViewProps) {
  const groups = groupDiffByKind(diff);

  if (groups.length === 0) {
    return <p className="text-sm text-[var(--text-muted)]">No changes were made to the base resume.</p>;
  }

  return (
    <div className="flex flex-col gap-6">
      {groups.map((group) => (
        <section key={group.kind} aria-label={group.label} className="flex flex-col gap-2">
          <h3 className="flex items-center gap-2 text-sm font-semibold text-[var(--text)]">
            {group.label}
            <Badge tone={KIND_TONE[group.kind]}>{group.entries.length}</Badge>
          </h3>
          <ul className="flex flex-col gap-2">
            {group.entries.map((entry, index) => {
              const isKeywordInjection = entry.kind === 'keywordsInjected';
              return (
                <li
                  key={`${entry.entityId}-${index}`}
                  className="rounded-[var(--radius-md)] border border-[var(--border)] bg-[var(--bg-elevated)] p-3"
                >
                  <div className="flex items-center justify-between gap-2">
                    <p className="font-mono text-xs text-[var(--text-faint)]">{entry.entityId}</p>
                    {isKeywordInjection && <Badge tone="info">Keywords woven in</Badge>}
                  </div>
                  {entry.before && (
                    <p className="mt-1.5 text-sm text-[var(--text-faint)] line-through decoration-[var(--danger)]">
                      {entry.before}
                    </p>
                  )}
                  {entry.after && <p className="mt-1 text-sm text-[var(--text)]">{entry.after}</p>}
                  {isKeywordInjection && entry.keywords && entry.keywords.length > 0 && (
                    <div className="mt-1.5 flex flex-wrap gap-1">
                      {entry.keywords.map((keyword) => (
                        <Badge key={keyword} tone="accent">
                          {keyword}
                        </Badge>
                      ))}
                    </div>
                  )}
                  {entry.rationale && (
                    <p className="mt-1.5 text-xs italic text-[var(--text-muted)]">{entry.rationale}</p>
                  )}
                </li>
              );
            })}
          </ul>
        </section>
      ))}
    </div>
  );
}
