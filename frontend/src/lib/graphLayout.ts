import type { GraphNodeStatus, GraphNodeTrace } from '@/api/types';
import { parseTimeSpanMs } from '@/lib/time';

export interface GraphLayoutNode {
  id: string;
  status: GraphNodeStatus;
  durationMs: number;
  inputTokens: number;
  outputTokens: number;
  error?: string;
  rank: number;
  column: number;
  x: number;
  y: number;
}

export interface GraphLayoutEdge {
  from: string;
  to: string;
  path: string;
}

export interface GraphLayout {
  nodes: GraphLayoutNode[];
  edges: GraphLayoutEdge[];
  rankCount: number;
  width: number;
  height: number;
}

/**
 * Dependency edges for the tailoring pipeline declared in TailoringGraphFactory
 * (docs/CONTRACTS.md §7). Keyed by node name -> the nodes it depends on.
 */
export const TAILORING_GRAPH_DEPENDENCIES: Record<string, string[]> = {
  'fetch-jd': [],
  'load-kb': [],
  'analyze-jd': ['fetch-jd'],
  'build-base': ['load-kb'],
  'score-experience': ['analyze-jd', 'build-base'],
  'score-projects': ['analyze-jd', 'build-base'],
  'score-skills': ['analyze-jd', 'build-base'],
  'ats-review': ['fetch-jd', 'build-base'],
  'build-brief': ['fetch-jd', 'score-experience', 'score-projects', 'score-skills', 'ats-review'],
  'propose-commands': ['build-brief'],
  'validate-commands': ['build-base', 'propose-commands'],
  'repair-commands': ['build-base', 'validate-commands'],
  'verify-fabrication': ['repair-commands'],
  'verify-coverage': ['repair-commands'],
  'execute-commands': ['repair-commands'],
  'enforce-page-budget': ['execute-commands', 'score-experience', 'score-projects', 'score-skills'],
  render: ['verify-fabrication', 'verify-coverage', 'enforce-page-budget'],
};

const COLUMN_WIDTH = 220;
const ROW_HEIGHT = 148;
const MARGIN_X = 40;
const MARGIN_Y = 60;
const NODE_HALF_HEIGHT = 44;

/**
 * Lays out a DAG trace on a rank/column grid. Rank is the longest-path depth
 * from a root (a node with no present dependencies), so nodes that only
 * depend on already-satisfied upstream work land on the same rank as their
 * siblings -- which is exactly the set the graph executor ran concurrently.
 * Dependents always land at least one rank below every dependency.
 *
 * Pure and deterministic: given the same traces and dependency map it always
 * produces the same coordinates, so it can be unit tested without a DOM.
 */
export function computeGraphLayout(
  traces: readonly GraphNodeTrace[],
  dependencies: Record<string, string[]> = TAILORING_GRAPH_DEPENDENCIES,
): GraphLayout {
  const present = new Set(traces.map((trace) => trace.node));
  const rankOf = new Map<string, number>();

  function resolveRank(node: string, visiting: Set<string>): number {
    const cached = rankOf.get(node);
    if (cached !== undefined) return cached;
    if (visiting.has(node)) return 0;
    visiting.add(node);
    const deps = (dependencies[node] ?? []).filter((dep) => present.has(dep));
    const rank = deps.length === 0 ? 0 : 1 + Math.max(...deps.map((dep) => resolveRank(dep, visiting)));
    rankOf.set(node, rank);
    return rank;
  }

  for (const trace of traces) {
    resolveRank(trace.node, new Set());
  }

  const rankBuckets = new Map<number, string[]>();
  for (const trace of traces) {
    const rank = rankOf.get(trace.node) ?? 0;
    const bucket = rankBuckets.get(rank) ?? [];
    bucket.push(trace.node);
    rankBuckets.set(rank, bucket);
  }

  const rankCount = rankBuckets.size === 0 ? 0 : Math.max(...rankBuckets.keys()) + 1;
  const maxColumns =
    rankBuckets.size === 0 ? 0 : Math.max(...Array.from(rankBuckets.values(), (bucket) => bucket.length));

  const columnOf = new Map<string, number>();
  for (const bucket of rankBuckets.values()) {
    bucket.forEach((node, index) => columnOf.set(node, index));
  }

  const width = Math.max(1, maxColumns) * COLUMN_WIDTH + MARGIN_X * 2;
  const height = Math.max(1, rankCount) * ROW_HEIGHT + MARGIN_Y * 2;

  const nodes: GraphLayoutNode[] = traces.map((trace) => {
    const rank = rankOf.get(trace.node) ?? 0;
    const column = columnOf.get(trace.node) ?? 0;
    const bucketSize = rankBuckets.get(rank)?.length ?? 1;
    const rowWidth = bucketSize * COLUMN_WIDTH;
    const startX = (width - rowWidth) / 2;
    return {
      id: trace.node,
      status: trace.status,
      durationMs: parseTimeSpanMs(trace.duration),
      inputTokens: trace.inputTokens,
      outputTokens: trace.outputTokens,
      error: trace.error,
      rank,
      column,
      x: startX + column * COLUMN_WIDTH + COLUMN_WIDTH / 2,
      y: MARGIN_Y + rank * ROW_HEIGHT,
    };
  });

  const nodeById = new Map(nodes.map((node) => [node.id, node]));
  const edges: GraphLayoutEdge[] = [];
  for (const trace of traces) {
    const deps = (dependencies[trace.node] ?? []).filter((dep) => present.has(dep));
    for (const dep of deps) {
      const from = nodeById.get(dep);
      const to = nodeById.get(trace.node);
      if (!from || !to) continue;
      edges.push({ from: from.id, to: to.id, path: buildEdgePath(from.x, from.y, to.x, to.y) });
    }
  }

  return { nodes, edges, rankCount, width, height };
}

function buildEdgePath(x1: number, y1: number, x2: number, y2: number): string {
  const startY = y1 + NODE_HALF_HEIGHT;
  const endY = y2 - NODE_HALF_HEIGHT;
  const midY = (startY + endY) / 2;
  return `M ${x1} ${startY} C ${x1} ${midY}, ${x2} ${midY}, ${x2} ${endY}`;
}
