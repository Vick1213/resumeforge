import type { TokenUsage } from '@/api/types';

export interface TokenUsagePanelProps {
  usage: TokenUsage;
}

const STATS: { key: keyof TokenUsage; label: string }[] = [
  { key: 'modelCalls', label: 'Model calls' },
  { key: 'inputTokens', label: 'Input tokens' },
  { key: 'outputTokens', label: 'Output tokens' },
  { key: 'cacheHits', label: 'Cache hits' },
];

export function TokenUsagePanel({ usage }: TokenUsagePanelProps) {
  const total = usage.inputTokens + usage.outputTokens;

  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {STATS.map((stat) => (
          <div key={stat.key} className="rounded-[var(--radius-md)] border border-[var(--border)] bg-[var(--bg-elevated)] p-3">
            <p className="text-xs text-[var(--text-muted)]">{stat.label}</p>
            <p className="mt-1 text-xl font-semibold tabular-nums text-[var(--text)]">{usage[stat.key]}</p>
          </div>
        ))}
      </div>
      <p className="rounded-[var(--radius-md)] bg-[var(--accent-soft)] px-3 py-2.5 text-xs text-[var(--accent-text)]">
        {total < 2000
          ? `Only ${total} tokens crossed the wire for this run — the model never sees full bullet prose, only entity IDs and a short brief, and returns commands instead of a resume.`
          : `${total} tokens crossed the wire for this run.`}
      </p>
    </div>
  );
}
