import { useState } from 'react';
import type { FormEvent } from 'react';
import { CheckSquare, Download, Github, Search, Square } from 'lucide-react';
import { useGithubImport } from '@/api/queries';
import type { GitHubRepoPreview } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Input } from '@/components/ui/Input';
import { Skeleton } from '@/components/ui/Skeleton';
import { cn } from '@/lib/utils';

export default function KnowledgeImport() {
  const githubImport = useGithubImport();
  const [username, setUsername] = useState('');
  const [repos, setRepos] = useState<GitHubRepoPreview[] | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [previewName, setPreviewName] = useState<string | undefined>(undefined);
  const [committed, setCommitted] = useState<string[] | null>(null);

  function handleFetch(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault();
    setCommitted(null);
    githubImport.mutate(
      { username, repos: [], commit: false },
      {
        onSuccess: (result) => {
          setRepos(result.repos);
          setSelected(new Set(result.repos.map((repo) => repo.repoName)));
          setPreviewName(result.repos[0]?.repoName);
        },
      },
    );
  }

  function toggle(repoName: string): void {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(repoName)) {
        next.delete(repoName);
      } else {
        next.add(repoName);
      }
      return next;
    });
  }

  function handleCommit(): void {
    if (!repos) return;
    githubImport.mutate(
      { username, repos: Array.from(selected), commit: true },
      {
        onSuccess: (result) => {
          setCommitted(result.committed);
          setRepos(null);
          setSelected(new Set());
        },
      },
    );
  }

  const previewRepo = repos?.find((repo) => repo.repoName === previewName);
  const isFetching = githubImport.isPending && repos === null;
  const isCommitting = githubImport.isPending && repos !== null;

  return (
    <div className="flex flex-col gap-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight text-[var(--text)]">Import from GitHub</h1>
        <p className="mt-1 text-sm text-[var(--text-muted)]">
          Pull your public repositories in as project knowledge-base entries.
        </p>
      </header>

      <Card>
        <form onSubmit={handleFetch} className="flex items-end gap-3 p-5">
          <div className="flex flex-1 flex-col gap-1.5">
            <label htmlFor="github-username" className="text-xs font-medium text-[var(--text-muted)]">
              GitHub username
            </label>
            <Input
              id="github-username"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              placeholder="octocat"
              required
            />
          </div>
          <Button type="submit" disabled={username.trim().length === 0 || githubImport.isPending}>
            <Search className="h-4 w-4" aria-hidden="true" />
            {isFetching ? 'Fetching…' : 'Fetch repositories'}
          </Button>
        </form>
      </Card>

      {githubImport.isError && <ErrorState description="Could not reach GitHub. Check the username and try again." />}

      {committed && (
        <div className="rounded-[var(--radius-md)] border border-[var(--success)] bg-[var(--success-soft)] px-4 py-3 text-sm text-[var(--success)]">
          Imported {committed.length} {committed.length === 1 ? 'repository' : 'repositories'}: {committed.join(', ')}
        </div>
      )}

      {isFetching && (
        <div className="flex flex-col gap-2">
          {Array.from({ length: 3 }, (_, index) => (
            <Skeleton key={index} className="h-20" />
          ))}
        </div>
      )}

      {repos === null && !isFetching && !committed && (
        <EmptyState
          icon={<Github className="h-6 w-6" />}
          title="No repositories fetched yet"
          description="Enter a GitHub username above and fetch to preview importable repositories."
        />
      )}

      {repos && (
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <div className="flex flex-col gap-3">
            <div className="flex items-center justify-between">
              <h2 className="text-sm font-semibold text-[var(--text)]">{repos.length} repositories found</h2>
              <span className="text-xs text-[var(--text-muted)]">{selected.size} selected</span>
            </div>
            <ul className="flex flex-col gap-2">
              {repos.map((repo) => {
                const isSelected = selected.has(repo.repoName);
                return (
                  <li key={repo.repoName}>
                    <div
                      className={cn(
                        'flex items-start gap-3 rounded-[var(--radius-md)] border bg-[var(--bg-elevated)] px-3 py-2.5',
                        previewName === repo.repoName ? 'border-[var(--accent)]' : 'border-[var(--border)]',
                      )}
                    >
                      <button
                        type="button"
                        role="checkbox"
                        aria-checked={isSelected}
                        aria-label={`Select ${repo.repoName}`}
                        onClick={() => toggle(repo.repoName)}
                        className="mt-0.5 shrink-0 text-[var(--accent)]"
                      >
                        {isSelected ? (
                          <CheckSquare className="h-4 w-4" aria-hidden="true" />
                        ) : (
                          <Square className="h-4 w-4 text-[var(--text-faint)]" aria-hidden="true" />
                        )}
                      </button>
                      <button type="button" onClick={() => setPreviewName(repo.repoName)} className="flex-1 text-left">
                        <p className="text-sm font-medium text-[var(--text)]">{repo.repoName}</p>
                        {repo.description && <p className="text-xs text-[var(--text-muted)]">{repo.description}</p>}
                        <div className="mt-1 flex gap-2">
                          {repo.language && <Badge tone="neutral">{repo.language}</Badge>}
                          <Badge tone="accent">★ {repo.stars}</Badge>
                        </div>
                      </button>
                    </div>
                  </li>
                );
              })}
            </ul>
            <Button onClick={handleCommit} disabled={selected.size === 0 || githubImport.isPending}>
              <Download className="h-4 w-4" aria-hidden="true" />
              {isCommitting ? 'Importing…' : `Import ${selected.size} ${selected.size === 1 ? 'repository' : 'repositories'}`}
            </Button>
          </div>
          <div className="flex flex-col gap-2">
            <h2 className="text-sm font-semibold text-[var(--text)]">Generated markdown preview</h2>
            {previewRepo ? (
              <pre className="max-h-[560px] overflow-auto rounded-[var(--radius-md)] border border-[var(--border)] bg-[var(--bg-inset)] p-4 font-mono text-xs text-[var(--text)]">
                {previewRepo.generatedMarkdown}
              </pre>
            ) : (
              <EmptyState
                title="Select a repository"
                description="Choose a repository on the left to preview its generated markdown."
              />
            )}
          </div>
        </div>
      )}
    </div>
  );
}
