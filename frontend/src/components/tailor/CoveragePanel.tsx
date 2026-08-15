import { CheckCircle2, XCircle } from 'lucide-react';
import type { CoverageReport } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { cn } from '@/lib/utils';

export interface CoveragePanelProps {
  coverage: CoverageReport;
}

export function CoveragePanel({ coverage }: CoveragePanelProps) {
  const percent = Math.round(coverage.score * 100);
  const coveredCount = coverage.requirements.filter((requirement) => requirement.covered).length;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-3">
        <div
          className="flex h-14 w-14 shrink-0 items-center justify-center rounded-full border-4 border-[var(--accent)] text-sm font-bold text-[var(--accent)]"
          role="img"
          aria-label={`Coverage score ${percent} percent`}
        >
          {percent}%
        </div>
        <div>
          <p className="text-sm font-semibold text-[var(--text)]">Requirement coverage</p>
          <p className="text-xs text-[var(--text-muted)]">
            {coveredCount} of {coverage.requirements.length} requirements evidenced
          </p>
        </div>
      </div>
      <ul className="flex flex-col gap-2">
        {coverage.requirements.map((requirement) => (
          <li
            key={requirement.requirementId}
            className={cn(
              'flex items-start gap-3 rounded-[var(--radius-md)] border px-3 py-2.5',
              requirement.covered
                ? 'border-[var(--success)] bg-[var(--success-soft)]'
                : 'border-[var(--border)] bg-[var(--bg-inset)]',
            )}
          >
            {requirement.covered ? (
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-[var(--success)]" aria-hidden="true" />
            ) : (
              <XCircle className="mt-0.5 h-4 w-4 shrink-0 text-[var(--text-faint)]" aria-hidden="true" />
            )}
            <div className="flex flex-1 flex-col gap-1.5">
              <p className="text-sm text-[var(--text)]">{requirement.requirementText}</p>
              {requirement.evidenceIds.length > 0 ? (
                <div className="flex flex-wrap gap-1">
                  {requirement.evidenceIds.map((evidence) => (
                    <Badge key={evidence} tone="accent" className="font-mono">
                      {evidence}
                    </Badge>
                  ))}
                </div>
              ) : (
                <span className="text-xs text-[var(--text-faint)]">No evidence found</span>
              )}
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}
