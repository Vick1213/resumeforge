import { CheckCircle2, XCircle } from 'lucide-react';
import type { CommandValidationResult, TailorCommand } from '@/api/types';
import { Badge } from '@/components/ui/Badge';

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
  }
}

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
              <p className="mt-1.5 text-sm text-[var(--text)]">{describeCommand(command)}</p>
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
          {commands.rejected.map((rejected, index) => (
            <li key={index} className="rounded-[var(--radius-md)] border border-[var(--danger)] bg-[var(--danger-soft)] p-3">
              <div className="flex items-center justify-between gap-2">
                <Badge tone="danger" className="font-mono">
                  {rejected.code}
                </Badge>
                <span className="text-xs font-mono text-[var(--text-faint)]">{rejected.command.op}</span>
              </div>
              <p className="mt-1.5 text-sm text-[var(--text)]">{rejected.reason}</p>
            </li>
          ))}
          {commands.rejected.length === 0 && <p className="text-sm text-[var(--text-faint)]">No commands rejected.</p>}
        </ul>
      </section>
    </div>
  );
}
