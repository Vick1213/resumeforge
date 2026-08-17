import type { ModelEffort } from '@/api/types';

/**
 * Display metadata for each `ModelEffort` level, sourced from the token
 * estimate table in CONTRACTS.md §6. Token counts are the contract's figures
 * verbatim — this is the "cost story" the tailoring UI is required to show
 * beside the effort control, not a UI-invented number.
 */
export interface EffortLevelInfo {
  value: ModelEffort;
  label: string;
  /** Typical model output tokens at this level, per CONTRACTS.md §6. */
  estimatedOutputTokens: number;
  /** One short line describing what the extra spend actually buys. */
  description: string;
}

export const EFFORT_LEVELS: readonly EffortLevelInfo[] = [
  {
    value: 'minimal',
    label: 'Minimal',
    estimatedOutputTokens: 200,
    description: 'Selection and ordering only — no rewritten prose.',
  },
  {
    value: 'standard',
    label: 'Standard',
    estimatedOutputTokens: 600,
    description: 'Adds rewrites and a tailored summary.',
  },
  {
    value: 'thorough',
    label: 'Thorough',
    estimatedOutputTokens: 1200,
    description: 'Adds keyword injection for skills your knowledge base backs.',
  },
  {
    value: 'maximum',
    label: 'Maximum',
    estimatedOutputTokens: 2000,
    description: 'Raises the rewrite cap furthest; every run gets a fresh summary.',
  },
  {
    value: 'full',
    label: 'Full AI',
    estimatedOutputTokens: 8000,
    description: 'Rewrites every bullet and project description for this job. No rewrite cap.',
  },
];

/** The contract default — reproduces pre-effort behaviour when sent as-is. */
export const DEFAULT_EFFORT: ModelEffort = 'standard';

export function formatTokenEstimate(tokens: number): string {
  return `~${tokens.toLocaleString()} tokens`;
}
