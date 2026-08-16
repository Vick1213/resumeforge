import type { DiffKind, ResumeDiffEntry } from '@/api/types';

export interface DiffGroup {
  kind: DiffKind;
  label: string;
  entries: ResumeDiffEntry[];
}

const KIND_ORDER: DiffKind[] = [
  'rewritten',
  'keywordsInjected',
  'variantSelected',
  'reordered',
  'included',
  'excluded',
  'summarySet',
  'skillEmphasized',
];

const KIND_LABELS: Record<DiffKind, string> = {
  included: 'Included',
  excluded: 'Excluded',
  reordered: 'Reordered',
  rewritten: 'Rewritten',
  variantSelected: 'Variant selected',
  summarySet: 'Summary set',
  skillEmphasized: 'Skills emphasized',
  keywordsInjected: 'Keywords injected',
};

/** Groups diff entries by DiffKind, ordered so prose changes surface first. */
export function groupDiffByKind(diff: readonly ResumeDiffEntry[]): DiffGroup[] {
  const buckets = new Map<DiffKind, ResumeDiffEntry[]>();
  for (const entry of diff) {
    const bucket = buckets.get(entry.kind) ?? [];
    bucket.push(entry);
    buckets.set(entry.kind, bucket);
  }

  return KIND_ORDER.filter((kind) => buckets.has(kind)).map((kind) => ({
    kind,
    label: KIND_LABELS[kind],
    entries: buckets.get(kind) ?? [],
  }));
}
