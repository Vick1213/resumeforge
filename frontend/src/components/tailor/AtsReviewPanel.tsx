import { ArrowRight, ListChecks, TriangleAlert, UserSearch } from 'lucide-react';
import type { AtsGap, AtsGapImportance, AtsReview } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { cn } from '@/lib/utils';

export interface AtsReviewPanelProps {
  review: AtsReview;
}

const importanceLabel: Record<AtsGapImportance, string> = {
  critical: 'Critical',
  important: 'Important',
  niceToHave: 'Nice to have',
};

const importanceTone: Record<AtsGapImportance, 'danger' | 'warning' | 'neutral'> = {
  critical: 'danger',
  important: 'warning',
  niceToHave: 'neutral',
};

/**
 * The screener's reading of the base resume against this posting: the score it starts at, the
 * score closing every gap would reach, and each gap in between.
 *
 * The two scores are shown together, and deliberately as a pair rather than as a single
 * number — the second one is the only thing that says whether this run was worth making. A
 * gap flagged `skillsOnly` gets its own marker for the same reason it exists in the model:
 * it is the one kind of gap that already passes the parser, so its fix is a bullet and never
 * another entry in the skills list.
 */
export function AtsReviewPanel({ review }: AtsReviewPanelProps) {
  const delta = review.scoreAfter - review.scoreBefore;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-4">
        <div className="flex items-center gap-2">
          <ScoreDial value={review.scoreBefore} label="ATS score now" tone="muted" />
          <ArrowRight className="h-4 w-4 text-[var(--text-faint)]" aria-hidden="true" />
          <ScoreDial value={review.scoreAfter} label="ATS score after fixes" tone="accent" />
        </div>
        <div className="flex-1">
          <p className="text-sm font-semibold text-[var(--text)]">
            {delta > 0 ? `+${delta} points available` : 'No headroom found'}
          </p>
          <p className="text-xs text-[var(--text-muted)]">{review.verdict}</p>
        </div>
      </div>

      {review.gaps.length > 0 ? (
        <section className="flex flex-col gap-2">
          <h3 className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-[var(--text-muted)]">
            <ListChecks className="h-3.5 w-3.5" aria-hidden="true" />
            What the posting asks for that the resume does not show
          </h3>
          <ul className="flex flex-col gap-2">
            {review.gaps.map((gap) => (
              <GapRow key={`${gap.keyword}-${gap.placement ?? ''}`} gap={gap} />
            ))}
          </ul>
        </section>
      ) : (
        <p className="text-sm text-[var(--text-muted)]">
          The review found nothing this posting asks for that the resume does not already evidence.
        </p>
      )}

      {review.recruiterNotes.length > 0 ? (
        <section className="flex flex-col gap-2">
          <h3 className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-[var(--text-muted)]">
            <UserSearch className="h-3.5 w-3.5" aria-hidden="true" />
            What a human reviewer would hold against it
          </h3>
          <ul className="flex flex-col gap-1.5">
            {review.recruiterNotes.map((note) => (
              <li
                key={note}
                className="rounded-[var(--radius-md)] border border-[var(--border)] bg-[var(--bg-inset)] px-3 py-2 text-sm text-[var(--text)]"
              >
                {note}
              </li>
            ))}
          </ul>
        </section>
      ) : null}
    </div>
  );
}

function GapRow({ gap }: { gap: AtsGap }) {
  return (
    <li className="flex flex-col gap-1.5 rounded-[var(--radius-md)] border border-[var(--border)] bg-[var(--bg-inset)] px-3 py-2.5">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm font-semibold text-[var(--text)]">{gap.keyword}</span>
        <Badge tone={importanceTone[gap.importance]}>{importanceLabel[gap.importance]}</Badge>
        {gap.skillsOnly ? (
          <Badge tone="neutral" className="flex items-center gap-1">
            <TriangleAlert className="h-3 w-3" aria-hidden="true" />
            Listed in skills, never evidenced
          </Badge>
        ) : null}
        {gap.placement ? (
          <Badge tone="accent" className="font-mono">
            {gap.placement}
          </Badge>
        ) : null}
      </div>
      <p className="text-sm text-[var(--text-muted)]">{gap.angle}</p>
    </li>
  );
}

function ScoreDial({ value, label, tone }: { value: number; label: string; tone: 'muted' | 'accent' }) {
  return (
    <div
      className={cn(
        'flex h-14 w-14 shrink-0 items-center justify-center rounded-full border-4 text-sm font-bold',
        tone === 'accent'
          ? 'border-[var(--accent)] text-[var(--accent)]'
          : 'border-[var(--border)] text-[var(--text-muted)]',
      )}
      role="img"
      aria-label={`${label}: ${value} out of 100`}
    >
      {value}
    </div>
  );
}
