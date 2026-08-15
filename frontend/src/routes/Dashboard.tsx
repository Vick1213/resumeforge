import { Link } from 'react-router-dom';
import { Award, Briefcase, FolderGit2, GraduationCap, Sparkles, ArrowUpRight, TriangleAlert } from 'lucide-react';
import { useApplications, useProfile } from '@/api/queries';
import { Card, CardContent } from '@/components/ui/Card';
import { Badge } from '@/components/ui/Badge';
import { Skeleton } from '@/components/ui/Skeleton';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { formatRelativeTime } from '@/lib/time';
import type { ApplicationStatus, KnowledgeCounts } from '@/api/types';

const COUNT_META: { key: keyof KnowledgeCounts; label: string; icon: typeof Briefcase }[] = [
  { key: 'experience', label: 'Experience', icon: Briefcase },
  { key: 'projects', label: 'Projects', icon: FolderGit2 },
  { key: 'education', label: 'Education', icon: GraduationCap },
  { key: 'certifications', label: 'Certifications', icon: Award },
  { key: 'skills', label: 'Skill groups', icon: Sparkles },
];

const FUNNEL_STAGES: { status: ApplicationStatus; label: string }[] = [
  { status: 'saved', label: 'Saved' },
  { status: 'applied', label: 'Applied' },
  { status: 'screening', label: 'Screening' },
  { status: 'interview', label: 'Interview' },
  { status: 'offer', label: 'Offer' },
  { status: 'rejected', label: 'Rejected' },
];

const linkButtonClass =
  'inline-flex h-9 items-center justify-center rounded-[var(--radius-sm)] bg-[var(--accent)] px-4 text-sm font-medium text-white transition-colors hover:bg-[var(--accent-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-[var(--accent)] focus-visible:outline-offset-2';

export default function Dashboard() {
  const profile = useProfile();
  const applications = useApplications();

  const recentRuns = (applications.data ?? [])
    .filter((application) => application.coverageScore !== undefined)
    .sort((a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime())
    .slice(0, 5);

  const funnelCounts = FUNNEL_STAGES.map((stage) => ({
    ...stage,
    count: (applications.data ?? []).filter((application) => application.status === stage.status).length,
  }));
  const maxFunnelCount = Math.max(1, ...funnelCounts.map((stage) => stage.count));

  return (
    <div className="flex flex-col gap-8">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight text-[var(--text)]">Dashboard</h1>
        <p className="mt-1 text-sm text-[var(--text-muted)]">
          An overview of your knowledge base, recent tailoring runs, and application pipeline.
        </p>
      </header>

      <section aria-labelledby="kb-counts-heading" className="flex flex-col gap-3">
        <h2 id="kb-counts-heading" className="text-sm font-semibold text-[var(--text)]">
          Knowledge base
        </h2>

        {profile.isLoading && (
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-5">
            {Array.from({ length: 5 }, (_, index) => (
              <Skeleton key={index} className="h-24" />
            ))}
          </div>
        )}

        {profile.isError && (
          <ErrorState description="Could not load your profile." onRetry={() => void profile.refetch()} />
        )}

        {profile.data &&
          (Object.values(profile.data.counts).every((count) => count === 0) ? (
            <EmptyState
              icon={<Sparkles className="h-6 w-6" />}
              title="Your knowledge base is empty"
              description="Add experience, projects, education, and skills to start tailoring resumes."
              action={
                <Link to="/knowledge" className={linkButtonClass}>
                  Add your first item
                </Link>
              }
            />
          ) : (
            <>
              <div className="grid grid-cols-2 gap-3 sm:grid-cols-5">
                {COUNT_META.map(({ key, label, icon: Icon }) => (
                  <div
                    key={key}
                    className="flex flex-col gap-2 rounded-[var(--radius-lg)] border border-[var(--border)] bg-[var(--bg-elevated)] p-4"
                  >
                    <Icon className="h-4 w-4 text-[var(--accent)]" aria-hidden="true" />
                    <span className="text-2xl font-semibold tabular-nums text-[var(--text)]">{profile.data.counts[key]}</span>
                    <span className="text-xs text-[var(--text-muted)]">{label}</span>
                  </div>
                ))}
              </div>
              {profile.data.diagnostics.length > 0 && (
                <div className="flex items-start gap-2 rounded-[var(--radius-md)] border border-[var(--warning)] bg-[var(--warning-soft)] px-3 py-2.5 text-xs text-[var(--warning)]">
                  <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                  <div className="flex flex-col gap-1">
                    {profile.data.diagnostics.map((diagnostic, index) => (
                      <span key={index}>
                        {diagnostic.file}:{diagnostic.line} — {diagnostic.message}
                      </span>
                    ))}
                  </div>
                </div>
              )}
            </>
          ))}
      </section>

      <section aria-labelledby="recent-runs-heading" className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <h2 id="recent-runs-heading" className="text-sm font-semibold text-[var(--text)]">
            Recent tailoring runs
          </h2>
          <Link
            to="/tailor"
            className="inline-flex items-center gap-1 text-xs font-medium text-[var(--accent)] hover:text-[var(--accent-hover)]"
          >
            New run
            <ArrowUpRight className="h-3 w-3" aria-hidden="true" />
          </Link>
        </div>

        {applications.isLoading && (
          <div className="flex flex-col gap-2">
            {Array.from({ length: 3 }, (_, index) => (
              <Skeleton key={index} className="h-16" />
            ))}
          </div>
        )}

        {applications.isError && (
          <ErrorState description="Could not load tailoring runs." onRetry={() => void applications.refetch()} />
        )}

        {applications.data && recentRuns.length === 0 && (
          <EmptyState
            icon={<Sparkles className="h-6 w-6" />}
            title="No tailoring runs yet"
            description="Paste a job description on the Tailor page to generate your first tailored resume."
            action={
              <Link to="/tailor" className={linkButtonClass}>
                Start tailoring
              </Link>
            }
          />
        )}

        {recentRuns.length > 0 && (
          <Card>
            <ul className="divide-y divide-[var(--border)]">
              {recentRuns.map((run) => (
                <li key={run.id} className="flex items-center justify-between gap-4 px-5 py-3">
                  <div>
                    <p className="text-sm font-medium text-[var(--text)]">
                      {run.title} · {run.company}
                    </p>
                    <p className="text-xs text-[var(--text-faint)]">{formatRelativeTime(run.updatedAt)}</p>
                  </div>
                  <div className="flex items-center gap-3">
                    {run.usage && (
                      <span className="tabular-nums text-xs text-[var(--text-muted)]">
                        {run.usage.inputTokens + run.usage.outputTokens} tok
                      </span>
                    )}
                    <Badge tone={(run.coverageScore ?? 0) >= 0.75 ? 'success' : 'warning'}>
                      {Math.round((run.coverageScore ?? 0) * 100)}% coverage
                    </Badge>
                  </div>
                </li>
              ))}
            </ul>
          </Card>
        )}
      </section>

      <section aria-labelledby="funnel-heading" className="flex flex-col gap-3">
        <h2 id="funnel-heading" className="text-sm font-semibold text-[var(--text)]">
          Application funnel
        </h2>

        {applications.isLoading && <Skeleton className="h-40" />}

        {applications.data &&
          (applications.data.length === 0 ? (
            <EmptyState
              icon={<Briefcase className="h-6 w-6" />}
              title="No applications tracked yet"
              description="Save a tailored resume as an application to start tracking it here."
              action={
                <Link to="/applications" className={linkButtonClass}>
                  Go to applications
                </Link>
              }
            />
          ) : (
            <Card>
              <CardContent className="flex flex-col gap-3 pt-5">
                {funnelCounts.map((stage) => (
                  <div key={stage.status} className="flex items-center gap-3">
                    <span className="w-24 shrink-0 text-xs text-[var(--text-muted)]">{stage.label}</span>
                    <div className="h-2 flex-1 overflow-hidden rounded-full bg-[var(--bg-inset)]">
                      <div
                        className="h-full rounded-full bg-[var(--accent)]"
                        style={{ width: `${(stage.count / maxFunnelCount) * 100}%` }}
                      />
                    </div>
                    <span className="w-6 text-right text-xs font-medium tabular-nums text-[var(--text)]">{stage.count}</span>
                  </div>
                ))}
              </CardContent>
            </Card>
          ))}
      </section>
    </div>
  );
}
