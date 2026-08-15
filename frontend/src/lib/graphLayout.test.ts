import { describe, expect, it } from 'vitest';
import { computeGraphLayout } from '@/lib/graphLayout';
import type { GraphNodeTrace } from '@/api/types';

function trace(node: string): GraphNodeTrace {
  return { node, status: 'succeeded', duration: '00:00:00.1000000', inputTokens: 0, outputTokens: 0 };
}

// a, b have no deps; c and d both depend on a and b (ran concurrently); e depends on c and d.
const dependencies: Record<string, string[]> = {
  a: [],
  b: [],
  c: ['a', 'b'],
  d: ['a', 'b'],
  e: ['c', 'd'],
};

describe('computeGraphLayout', () => {
  it('puts root nodes on rank 0 and nodes that ran concurrently on the same rank in different columns', () => {
    const layout = computeGraphLayout([trace('a'), trace('b'), trace('c'), trace('d')], dependencies);

    expect(layout.nodes.find((n) => n.id === 'a')?.rank).toBe(0);
    expect(layout.nodes.find((n) => n.id === 'b')?.rank).toBe(0);

    const c = layout.nodes.find((n) => n.id === 'c');
    const d = layout.nodes.find((n) => n.id === 'd');
    expect(c?.rank).toBe(1);
    expect(d?.rank).toBe(1);
    expect(c?.column).not.toBe(d?.column);
  });

  it('places a dependent strictly deeper than every dependency and draws one edge per satisfied dependency', () => {
    const traces = ['a', 'b', 'c', 'd', 'e'].map(trace);
    const layout = computeGraphLayout(traces, dependencies);

    const c = layout.nodes.find((n) => n.id === 'c');
    const d = layout.nodes.find((n) => n.id === 'd');
    const e = layout.nodes.find((n) => n.id === 'e');
    expect(e?.rank).toBeGreaterThan(c?.rank ?? -1);
    expect(e?.rank).toBeGreaterThan(d?.rank ?? -1);

    // c depends on a and b only (both present) -> exactly 2 edges into c
    expect(layout.edges.filter((edge) => edge.to === 'c')).toHaveLength(2);
    // an edge omitted from the trace should not be drawn
    const partialLayout = computeGraphLayout([trace('a'), trace('c')], dependencies);
    expect(partialLayout.edges.filter((edge) => edge.to === 'c')).toHaveLength(1);
  });
});
