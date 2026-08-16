import { useState } from 'react';
import type { FormEvent } from 'react';
import { BookmarkCheck, BookmarkPlus, Sparkles } from 'lucide-react';
import { useCreateApplication, useCreateJob, useJobAnalysis, useTailor } from '@/api/queries';
import type { JobPosting, ModelEffort, SeniorityLevel } from '@/api/types';
import { DEFAULT_EFFORT } from '@/lib/effort';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Input } from '@/components/ui/Input';
import { Skeleton } from '@/components/ui/Skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/Tabs';
import { Textarea } from '@/components/ui/Textarea';
import { GraphTracePanel } from '@/components/graph/GraphTracePanel';
import { CoveragePanel } from '@/components/tailor/CoveragePanel';
import { DiffView } from '@/components/tailor/DiffView';
import { CommandsPanel } from '@/components/tailor/CommandsPanel';
import { EffortControl } from '@/components/tailor/EffortControl';
import { TokenUsagePanel } from '@/components/tailor/TokenUsagePanel';
import { ExportButtons } from '@/components/tailor/ExportButtons';

const SENIORITY_LABELS: Record<SeniorityLevel, string> = {
  unknown: 'Unknown',
  intern: 'Intern',
  junior: 'Junior',
  mid: 'Mid-level',
  senior: 'Senior',
  staff: 'Staff',
  principal: 'Principal',
};

export default function Tailor() {
  const [inputMode, setInputMode] = useState<'url' | 'text'>('url');
  const [urlValue, setUrlValue] = useState('');
  const [textValue, setTextValue] = useState('');
  const [effort, setEffort] = useState<ModelEffort>(DEFAULT_EFFORT);
  const [job, setJob] = useState<JobPosting | undefined>(undefined);

  const createJob = useCreateJob();
  const jobAnalysis = useJobAnalysis(job?.id);
  const tailor = useTailor();
  const createApplication = useCreateApplication();

  const canAnalyze = inputMode === 'url' ? urlValue.trim().length > 0 : textValue.trim().length > 0;

  function handleAnalyze(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault();
    createJob.mutate(
      {
        url: inputMode === 'url' ? urlValue.trim() : undefined,
        rawText: inputMode === 'text' ? textValue.trim() : undefined,
      },
      { onSuccess: (created) => setJob(created) },
    );
  }

  function handleRunTailor(): void {
    if (!job) return;
    tailor.mutate({ jobId: job.id, effort, dryRun: false });
  }

  function handleSaveApplication(): void {
    if (!job || !tailor.data) return;
    createApplication.mutate({
      jobId: job.id,
      resumeId: tailor.data.document.id,
      company: job.company ?? 'Unknown company',
      title: job.title ?? 'Untitled role',
      jobUrl: job.sourceUrl,
      status: 'saved',
      coverageScore: tailor.data.coverage.score,
      usage: tailor.data.usage,
    });
  }

  return (
    <div className="flex flex-col gap-8">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight text-[var(--text)]">Tailor</h1>
        <p className="mt-1 text-sm text-[var(--text-muted)]">
          Paste a job posting and watch the graph engine analyze, score, and propose a tailored resume.
        </p>
      </header>

      <Card>
        <form onSubmit={handleAnalyze} className="flex flex-col gap-4 p-5">
          <Tabs value={inputMode} onValueChange={(value) => setInputMode(value as 'url' | 'text')}>
            <TabsList>
              <TabsTrigger value="url">Job URL</TabsTrigger>
              <TabsTrigger value="text">Paste text</TabsTrigger>
            </TabsList>
            <TabsContent value="url">
              <label htmlFor="job-url" className="sr-only">
                Job posting URL
              </label>
              <Input
                id="job-url"
                type="url"
                value={urlValue}
                onChange={(event) => setUrlValue(event.target.value)}
                placeholder="https://boards.greenhouse.io/vertexsystems/jobs/5821034"
              />
            </TabsContent>
            <TabsContent value="text">
              <label htmlFor="job-text" className="sr-only">
                Job description text
              </label>
              <Textarea
                id="job-text"
                value={textValue}
                onChange={(event) => setTextValue(event.target.value)}
                rows={8}
                placeholder="Paste the full job description here"
              />
            </TabsContent>
          </Tabs>
          <div className="flex items-center justify-end">
            <Button type="submit" disabled={!canAnalyze || createJob.isPending}>
              <Sparkles className="h-4 w-4" aria-hidden="true" />
              {createJob.isPending ? 'Analyzing…' : 'Analyze job'}
            </Button>
          </div>
        </form>
      </Card>

      {createJob.isError && <ErrorState description="Could not analyze that job posting." />}

      {!job && !createJob.isPending && (
        <EmptyState
          icon={<Sparkles className="h-6 w-6" />}
          title="Paste a job description to begin"
          description="ResumeForge analyzes requirements, scores your knowledge base against them, and proposes a tailored resume through a small dependency graph of deterministic and model-backed steps."
        />
      )}

      {job && (
        <Card>
          <div className="flex flex-col gap-4 p-5">
            {jobAnalysis.isLoading && <Skeleton className="h-28" />}
            {jobAnalysis.isError && (
              <ErrorState description="Could not load the job analysis." onRetry={() => void jobAnalysis.refetch()} />
            )}
            {jobAnalysis.data && (
              <>
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <h2 className="text-sm font-semibold text-[var(--text)]">
                      {job.title ?? 'Job analysis'} {job.company && `· ${job.company}`}
                    </h2>
                    <p className="text-xs text-[var(--text-muted)]">
                      {jobAnalysis.data.requirements.length} requirements detected · seniority:{' '}
                      {SENIORITY_LABELS[jobAnalysis.data.seniority]}
                    </p>
                  </div>
                </div>
                <div className="flex flex-wrap gap-1.5">
                  {jobAnalysis.data.keywords.slice(0, 10).map((keyword) => (
                    <Badge key={keyword} tone="neutral">
                      {keyword}
                    </Badge>
                  ))}
                </div>
                {jobAnalysis.data.missingSkills.length > 0 && (
                  <p className="text-xs text-[var(--warning)]">
                    Missing from your knowledge base: {jobAnalysis.data.missingSkills.join(', ')}
                  </p>
                )}
                <div className="flex flex-col gap-3 border-t border-[var(--border)] pt-4">
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <h3 className="text-xs font-semibold uppercase tracking-wide text-[var(--text-muted)]">
                      Model effort
                    </h3>
                    <Button onClick={handleRunTailor} disabled={tailor.isPending}>
                      <Sparkles className="h-4 w-4" aria-hidden="true" />
                      {tailor.isPending ? 'Tailoring…' : 'Run tailoring'}
                    </Button>
                  </div>
                  <EffortControl value={effort} onChange={setEffort} />
                </div>
              </>
            )}
          </div>
        </Card>
      )}

      {tailor.isError && <ErrorState description="The tailoring run failed." />}

      {tailor.isPending && (
        <div className="flex flex-col gap-3">
          <Skeleton className="h-8 w-56" />
          <Skeleton className="h-80" />
        </div>
      )}

      {tailor.data && (
        <section className="flex flex-col gap-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h2 className="text-lg font-semibold text-[var(--text)]">Results</h2>
            <div className="flex items-center gap-2">
              <Button variant="outline" size="sm" onClick={handleSaveApplication} disabled={createApplication.isPending}>
                {createApplication.isSuccess ? (
                  <BookmarkCheck className="h-4 w-4 text-[var(--success)]" aria-hidden="true" />
                ) : (
                  <BookmarkPlus className="h-4 w-4" aria-hidden="true" />
                )}
                {createApplication.isSuccess ? 'Saved' : 'Save as application'}
              </Button>
              <ExportButtons
                resumeId={tailor.data.document.id}
                fileBaseName={tailor.data.document.name.replace(/\s+/g, '-').toLowerCase()}
              />
            </div>
          </div>

          <Tabs defaultValue="graph">
            <TabsList>
              <TabsTrigger value="graph">Graph trace</TabsTrigger>
              <TabsTrigger value="coverage">Coverage</TabsTrigger>
              <TabsTrigger value="diff">Diff</TabsTrigger>
              <TabsTrigger value="commands">Commands</TabsTrigger>
              <TabsTrigger value="tokens">Token usage</TabsTrigger>
            </TabsList>
            <TabsContent value="graph">
              <GraphTracePanel trace={tailor.data.trace} />
            </TabsContent>
            <TabsContent value="coverage">
              <CoveragePanel coverage={tailor.data.coverage} />
            </TabsContent>
            <TabsContent value="diff">
              <DiffView diff={tailor.data.diff} />
            </TabsContent>
            <TabsContent value="commands">
              <CommandsPanel commands={tailor.data.commands} />
            </TabsContent>
            <TabsContent value="tokens">
              <TokenUsagePanel usage={tailor.data.usage} />
            </TabsContent>
          </Tabs>
        </section>
      )}
    </div>
  );
}
