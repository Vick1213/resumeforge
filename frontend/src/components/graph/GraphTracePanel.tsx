import { useId, useMemo } from 'react';
import type { GraphNodeStatus, GraphNodeTrace } from '@/api/types';
import { computeGraphLayout } from '@/lib/graphLayout';
import { formatDurationMs } from '@/lib/time';

const NODE_WIDTH = 176;
const NODE_HEIGHT = 88;

const STATUS_META: Record<GraphNodeStatus, { fill: string; border: string; label: string }> = {
  succeeded: { fill: 'var(--success-soft)', border: 'var(--success)', label: 'Succeeded' },
  failed: { fill: 'var(--danger-soft)', border: 'var(--danger)', label: 'Failed' },
  skipped: { fill: 'var(--bg-inset)', border: 'var(--border-strong)', label: 'Skipped' },
  cancelled: { fill: 'var(--warning-soft)', border: 'var(--warning)', label: 'Cancelled' },
};

const STATUS_ORDER: GraphNodeStatus[] = ['succeeded', 'failed', 'skipped', 'cancelled'];

function formatNodeLabel(nodeId: string): string {
  return nodeId
    .split('-')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

export interface GraphTracePanelProps {
  trace: GraphNodeTrace[];
}

export function GraphTracePanel({ trace }: GraphTracePanelProps) {
  const titleId = useId();
  const layout = useMemo(() => computeGraphLayout(trace), [trace]);

  if (trace.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-4 text-xs text-[var(--text-muted)]">
        {STATUS_ORDER.map((status) => (
          <span key={status} className="flex items-center gap-1.5">
            <span
              className="h-2.5 w-2.5 rounded-full"
              style={{ backgroundColor: STATUS_META[status].border }}
              aria-hidden="true"
            />
            {STATUS_META[status].label}
          </span>
        ))}
      </div>

      <div className="overflow-x-auto rounded-[var(--radius-lg)] border border-[var(--border)] bg-[var(--bg-inset)] p-4">
        <svg
          role="img"
          aria-labelledby={titleId}
          width={layout.width}
          height={layout.height}
          viewBox={`0 0 ${layout.width} ${layout.height}`}
          className="block"
        >
          <title id={titleId}>{`Tailoring pipeline execution graph, ${layout.nodes.length} nodes`}</title>
          <defs>
            <marker id="graph-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
              <path d="M 0 0 L 10 5 L 0 10 z" fill="var(--text-faint)" />
            </marker>
          </defs>
          {layout.edges.map((edge) => (
            <path
              key={`${edge.from}->${edge.to}`}
              d={edge.path}
              fill="none"
              stroke="var(--text-faint)"
              strokeWidth={1.5}
              markerEnd="url(#graph-arrow)"
            />
          ))}
          {layout.nodes.map((node) => {
            const meta = STATUS_META[node.status];
            const hasTokens = node.inputTokens > 0 || node.outputTokens > 0;
            const metaY = hasTokens || node.error ? 42 : 50;
            return (
              <g key={node.id} transform={`translate(${node.x - NODE_WIDTH / 2}, ${node.y - NODE_HEIGHT / 2})`}>
                <title>
                  {`${formatNodeLabel(node.id)} — ${meta.label}, ${formatDurationMs(node.durationMs)}`}
                  {hasTokens ? `, ${node.inputTokens + node.outputTokens} tokens` : ''}
                  {node.error ? `. Error: ${node.error}` : ''}
                </title>
                <rect width={NODE_WIDTH} height={NODE_HEIGHT} rx={12} fill={meta.fill} stroke={meta.border} strokeWidth={1.5} />
                <circle cx={NODE_WIDTH - 16} cy={17} r={4} fill={meta.border} />
                <text x={12} y={22} className="text-[12px] font-semibold fill-[var(--text)]">
                  {formatNodeLabel(node.id)}
                </text>
                <text x={12} y={metaY} className="text-[10px] fill-[var(--text-muted)]">
                  {`${meta.label} · ${formatDurationMs(node.durationMs)}`}
                </text>
                {hasTokens && (
                  <text x={12} y={62} className="text-[10px] fill-[var(--accent-text)]">
                    {`${node.inputTokens}→${node.outputTokens} tok`}
                  </text>
                )}
                {node.error && (
                  <text x={12} y={hasTokens ? 78 : 62} className="text-[10px] fill-[var(--danger)]">
                    {node.error.length > 24 ? `${node.error.slice(0, 24)}…` : node.error}
                  </text>
                )}
              </g>
            );
          })}
        </svg>
      </div>

      <table className="sr-only">
        <caption>Tailoring pipeline node details</caption>
        <thead>
          <tr>
            <th>Node</th>
            <th>Status</th>
            <th>Duration</th>
            <th>Input tokens</th>
            <th>Output tokens</th>
          </tr>
        </thead>
        <tbody>
          {layout.nodes.map((node) => (
            <tr key={node.id}>
              <td>{formatNodeLabel(node.id)}</td>
              <td>{STATUS_META[node.status].label}</td>
              <td>{formatDurationMs(node.durationMs)}</td>
              <td>{node.inputTokens}</td>
              <td>{node.outputTokens}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
