import { CheckCircle2, XCircle } from 'lucide-react';
import type { BadgeTone } from '@/components/ui/Badge';
import type { CommandValidationResult, RejectionCode, TailorCommand } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { cn } from '@/lib/utils';

function describeCommand(command: TailorCommand): string {
  switch (command.op) {
    case 'include':
      return `Include ${command.targets.join(', ')}`;
    case 'exclude':
      return `Exclude ${command.targets.join(', ')}`;
    case 'order':
      return `Order children of ${command.parent}: ${command.order.join(' → ')}`;
    case 'selectVariant':
      return `Select variant ${command.variantIndex} for ${command.target}`;
    case 'rewrite':
      return `Rewrite ${command.target}: "${command.text}"`;
    case 'setSummary':
      return `Set summary: "${command.text}"`;
    case 'emphasizeSkills':
      return `Emphasize ${command.skills.join(', ')}`;
    case 'setSectionOrder':
      return `Reorder sections: ${command.order.join(' → ')}`;
    case 'injectKeywords':
      return `Weave keywords into ${command.target}: "${command.text}"`;
  }
}

/**
 * `unsupported-keyword` is the fabrication guard working as intended, not a
 * failure — the model asked for a keyword the knowledge base doesn't
 * evidence, and the system said no. `op-unavailable-at-effort` is a
 * configuration gap, not a guard trip. Both get a distinct tone and a plain
 * explanation instead of blending into the generic "rejected" red.
 */
const REJECTION_TONE: Partial<Record<RejectionCode, BadgeTone>> = {
  'unsupported-keyword': 'info',
  'op-unavailable-at-effort': 'warning',
};

const REJECTION_BORDER: Partial<Record<RejectionCode, string>> = {
  'unsupported-keyword': 'border-[var(--info)] bg-[var(--info-soft)]',
  'op-unavailable-at-effort': 'border-[var(--warning)] bg-[var(--warning-soft)]',
};

const REJECTION_EXPLANATION: Partial<Record<RejectionCode, string>> = {
  'unsupported-keyword':
    'This is the fabrication guard working as intended: the model tried to add a keyword your knowledge base ' +
    "doesn't evidence anywhere, and the system refused rather than let it onto your resume.",
  'op-unavailable-at-effort':
    'Keyword injection only runs at Thorough effort or above. Raise the effort level and re-run tailoring to enable it.',
};

export interface CommandsPanelProps {
  commands: CommandValidationResult;
}

export function CommandsPanel({ commands }: CommandsPanelProps) {
  return (
    <div className="grid gap-6 md:grid-cols-2">
      <section className="flex flex-col gap-2">
        <h3 className="flex items-center gap-2 text-sm font-semibold text-[var(--text)]">
          <CheckCircle2 className="h-4 w-4 text-[var(--success)]" aria-hidden="true" />
          Accepted
          <Badge tone="success">{commands.accepted.length}</Badge>
        </h3>
        <ul className="flex flex-col gap-2">
          {commands.accepted.map((command, index) => (
            <li
              key={index}
              className="rounded-[var(--radius-md)] border border-[var(--border)] bg-[var(--bg-elevated)] p-3"
            >
              <Badge tone="accent" className="font-mono">
                {command.op}
              </Badge>
              {command.op === 'injectKeywords' ? (
                <>
                  <p className="mt-1.5 text-sm text-[var(--text)]">{command.text}</p>
                  <div className="mt-1.5 flex flex-wrap items-center gap-1">
                    <span className="text-xs text-[var(--text-faint)]">Keywords woven in:</span>
                    {command.keywords.map((keyword) => (
                      <Badge key={keyword} tone="accent">
                        {keyword}
                      </Badge>
                    ))}
                  </div>
                </>
              ) : (
                <p className="mt-1.5 text-sm text-[var(--text)]">{describeCommand(command)}</p>
              )}
              {command.rationale && <p className="mt-1 text-xs italic text-[var(--text-muted)]">{command.rationale}</p>}
            </li>
          ))}
          {commands.accepted.length === 0 && <p className="text-sm text-[var(--text-faint)]">No commands accepted.</p>}
        </ul>
      </section>
      <section className="flex flex-col gap-2">
        <h3 className="flex items-center gap-2 text-sm font-semibold text-[var(--text)]">
          <XCircle className="h-4 w-4 text-[var(--danger)]" aria-hidden="true" />
          Rejected
          <Badge tone="danger">{commands.rejected.length}</Badge>
        </h3>
        <ul className="flex flex-col gap-2">
          {commands.rejected.map((rejected, index) => {
            const explanation = REJECTION_EXPLANATION[rejected.code];
            return (
              <li
                key={index}
                className={cn(
                  'rounded-[var(--radius-md)] border p-3',
                  REJECTION_BORDER[rejected.code] ?? 'border-[var(--danger)] bg-[var(--danger-soft)]',
                )}
              >
                <div className="flex items-center justify-between gap-2">
                  <Badge tone={REJECTION_TONE[rejected.code] ?? 'danger'} className="font-mono">
                    {rejected.code}
                  </Badge>
                  <span className="text-xs font-mono text-[var(--text-faint)]">{rejected.command.op}</span>
                </div>
                <p className="mt-1.5 text-sm text-[var(--text)]">{rejected.reason}</p>
                {explanation && <p className="mt-1.5 text-xs text-[var(--text-muted)]">{explanation}</p>}
                {rejected.command.op === 'injectKeywords' && rejected.command.keywords.length > 0 && (
                  <div className="mt-1.5 flex flex-wrap gap-1">
                    {rejected.command.keywords.map((keyword) => (
                      <Badge key={keyword} tone="neutral">
                        {keyword}
                      </Badge>
                    ))}
                  </div>
                )}
              </li>
            );
          })}
          {commands.rejected.length === 0 && <p className="text-sm text-[var(--text-faint)]">No commands rejected.</p>}
        </ul>
      </section>
    </div>
  );
}
