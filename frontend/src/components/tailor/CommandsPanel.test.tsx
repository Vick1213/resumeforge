import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { CommandsPanel } from '@/components/tailor/CommandsPanel';
import type { CommandValidationResult } from '@/api/types';

describe('CommandsPanel', () => {
  it('renders an injectKeywords command with its keywords visible', () => {
    const commands: CommandValidationResult = {
      accepted: [
        {
          op: 'injectKeywords',
          target: 'exp:acme-corp#1',
          keywords: ['kubernetes', 'distributed systems'],
          text: 'Led migration of 40+ services on a Kubernetes-based distributed systems platform.',
          rationale: 'Surfaces two mandatory keywords already evidenced by this entry',
        },
      ],
      rejected: [],
    };

    render(<CommandsPanel commands={commands} />);

    expect(screen.getByText('Led migration of 40+ services on a Kubernetes-based distributed systems platform.')).toBeInTheDocument();
    expect(screen.getByText('kubernetes')).toBeInTheDocument();
    expect(screen.getByText('distributed systems')).toBeInTheDocument();
  });

  it('renders an unsupported-keyword rejection with a clear explanation', () => {
    const commands: CommandValidationResult = {
      accepted: [],
      rejected: [
        {
          command: {
            op: 'injectKeywords',
            target: 'exp:nimbus-systems#0',
            keywords: ['rust'],
            text: 'Designed an event-sourced billing pipeline in Rust.',
          },
          reason: 'Keyword "rust" is not evidenced anywhere in the knowledge base.',
          code: 'unsupported-keyword',
        },
      ],
    };

    render(<CommandsPanel commands={commands} />);

    expect(screen.getByText('unsupported-keyword')).toBeInTheDocument();
    expect(screen.getByText('Keyword "rust" is not evidenced anywhere in the knowledge base.')).toBeInTheDocument();
    expect(screen.getByText(/fabrication guard working as intended/)).toBeInTheDocument();
  });

  it('renders an op-unavailable-at-effort rejection explaining the effort gate', () => {
    const commands: CommandValidationResult = {
      accepted: [],
      rejected: [
        {
          command: {
            op: 'injectKeywords',
            target: 'exp:acme-corp#0',
            keywords: ['postgresql'],
            text: 'Cut p99 checkout latency on PostgreSQL.',
          },
          reason: 'injectKeywords requires Thorough effort or above.',
          code: 'op-unavailable-at-effort',
        },
      ],
    };

    render(<CommandsPanel commands={commands} />);

    expect(screen.getByText(/only runs at Thorough effort or above/)).toBeInTheDocument();
  });
});
