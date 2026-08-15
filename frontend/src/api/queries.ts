import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import * as api from '@/api/client';
import * as fixtures from '@/api/fixtures';
import { splitMarkdown, validateFrontmatter } from '@/lib/frontmatter';
import type {
  ApplicationDto,
  CreateApplicationRequest,
  CreateJobRequest,
  GitHubImportRequest,
  GitHubImportResult,
  JobPosting,
  KnowledgeItemDetailDto,
  KnowledgeItemDto,
  KnowledgeItemKind,
  RenderFormat,
  ResumeBasics,
  TailorRequest,
  UpdateApplicationRequest,
  UpsertKnowledgeRequest,
} from '@/api/types';

/**
 * Single seam between the query layer and the two possible data sources.
 * Every hook below routes through withFixtures() instead of branching on
 * VITE_USE_FIXTURES itself, so there is exactly one place that decides
 * whether a call hits the network or the in-memory fixture store.
 */
const USE_FIXTURES = import.meta.env.VITE_USE_FIXTURES !== 'false';
const FIXTURE_DELAY_MS = 260;

function withFixtures<T>(real: () => Promise<T>, fixture: () => T | Promise<T>): Promise<T> {
  if (!USE_FIXTURES) return real();
  return new Promise((resolve) => {
    setTimeout(() => resolve(fixture()), FIXTURE_DELAY_MS);
  });
}

// ---------------------------------------------------------------------------
// In-memory fixture stores (only ever touched when USE_FIXTURES is true) so
// that mutations in demo mode feel real across a session without a backend.
// ---------------------------------------------------------------------------

const knowledgeStore = new Map<string, KnowledgeItemDetailDto>(
  fixtures.fixtureKnowledgeItems.map((item) => [item.id, fixtures.seedKnowledgeDetail(item)]),
);

function stringArray(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((entry): entry is string => typeof entry === 'string') : [];
}

function stringField(value: unknown, fallback: string): string {
  return typeof value === 'string' && value.trim().length > 0 ? value : fallback;
}

function optionalStringField(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim().length > 0 ? value : undefined;
}

function titleFor(
  kind: KnowledgeItemKind,
  frontmatter: Record<string, unknown>,
  fallbackId: string,
): { title: string; subtitle?: string } {
  switch (kind) {
    case 'experience':
      return { title: stringField(frontmatter['role'], fallbackId), subtitle: optionalStringField(frontmatter['organization']) };
    case 'project':
      return { title: stringField(frontmatter['name'], fallbackId), subtitle: optionalStringField(frontmatter['tagline']) };
    case 'education':
      return { title: stringField(frontmatter['credential'], fallbackId), subtitle: optionalStringField(frontmatter['institution']) };
    case 'certification':
      return { title: stringField(frontmatter['name'], fallbackId), subtitle: optionalStringField(frontmatter['issuer']) };
    case 'basics':
      return { title: stringField(frontmatter['fullName'], fallbackId), subtitle: optionalStringField(frontmatter['headline']) };
  }
}

function buildKnowledgeDetail(itemId: string, rawMarkdown: string): KnowledgeItemDetailDto {
  const { frontmatter } = splitMarkdown(rawMarkdown);
  const validation = validateFrontmatter(frontmatter);
  const rawType = frontmatter['type'];
  const kind: KnowledgeItemKind =
    rawType === 'project' || rawType === 'education' || rawType === 'certification' || rawType === 'basics'
      ? rawType
      : 'experience';
  const { title, subtitle } = titleFor(kind, frontmatter, itemId);

  return {
    id: itemId,
    kind,
    title,
    subtitle,
    tech: stringArray(frontmatter['tech']),
    tags: stringArray(frontmatter['tags']),
    source: frontmatter['source'] === 'github' ? 'github' : 'manual',
    updatedAt: new Date().toISOString(),
    rawMarkdown,
    diagnostics: validation.issues.map((issue) => ({
      file: itemId,
      line: 0,
      message: issue.message,
      severity: issue.severity,
    })),
  };
}

function toSummary(detail: KnowledgeItemDetailDto): KnowledgeItemDto {
  return {
    id: detail.id,
    kind: detail.kind,
    title: detail.title,
    subtitle: detail.subtitle,
    tech: detail.tech,
    tags: detail.tags,
    source: detail.source,
    updatedAt: detail.updatedAt,
  };
}

function listFixtureKnowledge(): KnowledgeItemDto[] {
  return Array.from(knowledgeStore.values(), toSummary);
}

function getFixtureKnowledgeDetail(itemId: string): KnowledgeItemDetailDto {
  const existing = knowledgeStore.get(itemId);
  if (existing) return existing;
  throw new api.ApiError(404, { title: 'Not found', status: 404, detail: `Knowledge item ${itemId} not found` });
}

function upsertFixtureKnowledge(itemId: string, rawMarkdown: string): KnowledgeItemDetailDto {
  const detail = buildKnowledgeDetail(itemId, rawMarkdown);
  knowledgeStore.set(itemId, detail);
  return detail;
}

function fixtureGithubImport(request: GitHubImportRequest): GitHubImportResult {
  const selected =
    request.repos.length > 0
      ? fixtures.fixtureGithubRepos.filter((repo) => request.repos.includes(repo.repoName))
      : fixtures.fixtureGithubRepos;

  if (!request.commit) {
    return { repos: selected, committed: [] };
  }

  for (const repo of selected) {
    const itemId = `prj:${repo.repoName}`;
    knowledgeStore.set(itemId, buildKnowledgeDetail(itemId, repo.generatedMarkdown));
  }

  return { repos: [], committed: selected.map((repo) => repo.repoName) };
}

function fixtureCreateJob(request: CreateJobRequest): JobPosting {
  return {
    ...fixtures.fixtureJobPosting,
    sourceUrl: request.url ?? fixtures.fixtureJobPosting.sourceUrl,
    rawText: request.rawText ?? fixtures.fixtureJobPosting.rawText,
    fetchedAt: new Date().toISOString(),
  };
}

let applicationsStore: ApplicationDto[] = fixtures.fixtureApplications.map((application) => ({ ...application }));

function listFixtureApplications(): ApplicationDto[] {
  return applicationsStore.map((application) => ({ ...application }));
}

function createFixtureApplication(request: CreateApplicationRequest): ApplicationDto {
  const now = new Date().toISOString();
  const created: ApplicationDto = {
    id: `app-${Math.random().toString(36).slice(2, 9)}`,
    jobId: request.jobId,
    resumeId: request.resumeId,
    company: request.company,
    title: request.title,
    status: request.status ?? 'saved',
    jobUrl: request.jobUrl,
    location: request.location,
    notes: request.notes,
    coverageScore: request.coverageScore,
    usage: request.usage,
    createdAt: now,
    updatedAt: now,
  };
  applicationsStore = [created, ...applicationsStore];
  return { ...created };
}

function updateFixtureApplication(appId: string, request: UpdateApplicationRequest): ApplicationDto {
  const index = applicationsStore.findIndex((application) => application.id === appId);
  const current = index === -1 ? undefined : applicationsStore[index];
  if (!current) {
    throw new api.ApiError(404, { title: 'Not found', status: 404, detail: `Application ${appId} not found` });
  }
  const updated: ApplicationDto = {
    ...current,
    ...(request.status !== undefined ? { status: request.status } : {}),
    ...(request.notes !== undefined ? { notes: request.notes } : {}),
    ...(request.appliedAt !== undefined ? { appliedAt: request.appliedAt } : {}),
    updatedAt: new Date().toISOString(),
  };
  applicationsStore = applicationsStore.map((application, i) => (i === index ? updated : application));
  return { ...updated };
}

// ---------------------------------------------------------------------------
// Profile
// ---------------------------------------------------------------------------

export function useProfile() {
  return useQuery({
    queryKey: ['profile'],
    queryFn: () => withFixtures(api.getProfile, () => fixtures.fixtureProfile),
  });
}

export function useUpdateBasics() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (basics: ResumeBasics) =>
      withFixtures(
        () => api.updateBasics(basics),
        () => basics,
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['profile'] });
    },
  });
}

// ---------------------------------------------------------------------------
// Knowledge base
// ---------------------------------------------------------------------------

export function useKnowledge() {
  return useQuery({
    queryKey: ['knowledge'],
    queryFn: () => withFixtures(api.listKnowledge, listFixtureKnowledge),
  });
}

export function useKnowledgeItem(itemId: string | undefined) {
  return useQuery({
    queryKey: ['knowledge', itemId ?? null],
    queryFn: () => {
      const resolved = itemId ?? '';
      return withFixtures(
        () => api.getKnowledgeItem(resolved),
        () => getFixtureKnowledgeDetail(resolved),
      );
    },
    enabled: itemId !== undefined,
  });
}

export function useUpsertKnowledge() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ itemId, request }: { itemId: string; request: UpsertKnowledgeRequest }) =>
      withFixtures(
        () => api.upsertKnowledge(itemId, request),
        () => upsertFixtureKnowledge(itemId, request.rawMarkdown),
      ),
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({ queryKey: ['knowledge'] });
      void queryClient.invalidateQueries({ queryKey: ['knowledge', variables.itemId] });
    },
  });
}

export function useGithubImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: GitHubImportRequest) =>
      withFixtures(
        () => api.importGithub(request),
        () => fixtureGithubImport(request),
      ),
    onSuccess: (data) => {
      if (data.committed.length > 0) {
        void queryClient.invalidateQueries({ queryKey: ['knowledge'] });
      }
    },
  });
}

// ---------------------------------------------------------------------------
// Resumes
// ---------------------------------------------------------------------------

export function useResumes() {
  return useQuery({
    queryKey: ['resumes'],
    queryFn: () => withFixtures(api.listResumes, () => fixtures.fixtureResumeSummaries),
  });
}

export function useResume(resumeId: string | undefined) {
  return useQuery({
    queryKey: ['resumes', resumeId ?? null],
    queryFn: () => {
      const resolved = resumeId ?? '';
      return withFixtures(
        () => api.getResume(resolved),
        () => (resolved === fixtures.fixtureTailoringResult.document.id ? fixtures.fixtureTailoringResult.document : fixtures.fixtureBaseResume),
      );
    },
    enabled: resumeId !== undefined,
  });
}

export function useRebuildBase() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => withFixtures(api.rebuildBaseResume, () => fixtures.fixtureBaseResume),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['resumes'] });
    },
  });
}

// ---------------------------------------------------------------------------
// Jobs + tailoring
// ---------------------------------------------------------------------------

export function useCreateJob() {
  return useMutation({
    mutationFn: (request: CreateJobRequest) =>
      withFixtures(
        () => api.createJob(request),
        () => fixtureCreateJob(request),
      ),
  });
}

export function useJobAnalysis(jobId: string | undefined) {
  return useQuery({
    queryKey: ['jobs', jobId ?? null, 'analysis'],
    queryFn: () => {
      const resolved = jobId ?? '';
      return withFixtures(
        () => api.getJobAnalysis(resolved),
        () => fixtures.fixtureJobAnalysis,
      );
    },
    enabled: jobId !== undefined,
  });
}

export function useTailor() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: TailorRequest) =>
      withFixtures(
        () => api.runTailor(request),
        () => fixtures.fixtureTailoringResult,
      ),
    onSuccess: (_data, variables) => {
      if (!variables.dryRun) {
        void queryClient.invalidateQueries({ queryKey: ['resumes'] });
      }
    },
  });
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

function buildFixtureResumeText(resumeId: string): string {
  const doc =
    resumeId === fixtures.fixtureTailoringResult.document.id
      ? fixtures.fixtureTailoringResult.document
      : fixtures.fixtureBaseResume;

  const lines: string[] = [doc.basics.fullName];
  if (doc.basics.headline) lines.push(doc.basics.headline);
  lines.push('');
  if (doc.summary) {
    lines.push(doc.summary, '');
  }
  lines.push('EXPERIENCE');
  for (const experience of doc.experience) {
    if (!experience.included) continue;
    lines.push(`${experience.role} — ${experience.organization}`);
    for (const bullet of experience.bullets) {
      lines.push(`  - ${bullet.text}`);
    }
  }
  lines.push('', 'PROJECTS');
  for (const project of doc.projects) {
    if (!project.included) continue;
    lines.push(project.name);
    for (const bullet of project.bullets) {
      lines.push(`  - ${bullet.text}`);
    }
  }
  return lines.join('\n');
}

function fixtureRenderBlob(resumeId: string, format: RenderFormat): Blob {
  const text = buildFixtureResumeText(resumeId);
  switch (format) {
    case 'html':
      return new Blob([`<!doctype html><html><body><pre>${escapeHtml(text)}</pre></body></html>`], {
        type: 'text/html',
      });
    case 'pdf':
      return new Blob([text], { type: 'application/pdf' });
    case 'docx':
      return new Blob([text], {
        type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      });
    case 'md':
    default:
      return new Blob([text], { type: 'text/markdown' });
  }
}

export function useRenderResume() {
  return useMutation({
    mutationFn: ({ resumeId, format }: { resumeId: string; format: RenderFormat }) =>
      withFixtures(
        () => api.renderResume(resumeId, format),
        () => fixtureRenderBlob(resumeId, format),
      ),
  });
}

export function useTailorTrace(runId: string | undefined) {
  return useQuery({
    queryKey: ['tailor', runId ?? null, 'trace'],
    queryFn: () => {
      const resolved = runId ?? '';
      return withFixtures(
        () => api.getTailorTrace(resolved),
        () => fixtures.fixtureGraphTrace,
      );
    },
    enabled: runId !== undefined,
  });
}

// ---------------------------------------------------------------------------
// Applications
// ---------------------------------------------------------------------------

export function useApplications() {
  return useQuery({
    queryKey: ['applications'],
    queryFn: () => withFixtures(api.listApplications, listFixtureApplications),
  });
}

export function useCreateApplication() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateApplicationRequest) =>
      withFixtures(
        () => api.createApplication(request),
        () => createFixtureApplication(request),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['applications'] });
    },
  });
}

export function useUpdateApplication() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id: appId, request }: { id: string; request: UpdateApplicationRequest }) =>
      withFixtures(
        () => api.updateApplication(appId, request),
        () => updateFixtureApplication(appId, request),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['applications'] });
    },
  });
}
