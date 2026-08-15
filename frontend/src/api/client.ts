import type {
  ApplicationDto,
  AutofillProfile,
  CreateApplicationRequest,
  CreateJobRequest,
  GitHubImportRequest,
  GitHubImportResult,
  JobAnalysis,
  JobPosting,
  KnowledgeItemDetailDto,
  KnowledgeItemDto,
  LearnedFieldMap,
  ProblemDetails,
  ProfileDto,
  RenderFormat,
  ResolveFieldsRequest,
  ResolveFieldsResponse,
  ResumeBasics,
  ResumeDocument,
  ResumeSummaryDto,
  TailorRequest,
  TailoringResult,
  UpdateApplicationRequest,
  UpsertKnowledgeRequest,
} from '@/api/types';

const BASE_URL = import.meta.env.VITE_API_BASE ?? '/api';

/** Carries the RFC 9457 ProblemDetails fields from a failed API call. */
export class ApiError extends Error {
  readonly status: number;
  readonly problemType?: string;
  readonly title?: string;
  readonly detail?: string;
  readonly instance?: string;

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.problemType = problem.type;
    this.title = problem.title;
    this.detail = problem.detail;
    this.instance = problem.instance;
  }
}

async function toApiError(response: Response): Promise<ApiError> {
  const contentType = response.headers.get('content-type') ?? '';
  if (contentType.includes('json')) {
    try {
      const body = (await response.json()) as ProblemDetails;
      return new ApiError(response.status, body);
    } catch {
      // response claimed JSON but wasn't parseable; fall through
    }
  }
  return new ApiError(response.status, { status: response.status, title: response.statusText });
}

function buildInit(init: RequestInit | undefined, hasBody: boolean): RequestInit {
  return {
    ...init,
    headers: {
      Accept: 'application/json',
      ...(hasBody ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
  };
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE_URL}${path}`, buildInit(init, init?.body !== undefined));

  if (!response.ok) {
    throw await toApiError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  return (text.length > 0 ? JSON.parse(text) : undefined) as T;
}

async function requestBlob(path: string, init?: RequestInit): Promise<Blob> {
  const response = await fetch(`${BASE_URL}${path}`, buildInit(init, init?.body !== undefined));

  if (!response.ok) {
    throw await toApiError(response);
  }

  return response.blob();
}

function json(body: unknown): string {
  return JSON.stringify(body);
}

const id = (value: string): string => encodeURIComponent(value);

// ---------------------------------------------------------------------------
// Profile
// ---------------------------------------------------------------------------

export function getProfile(): Promise<ProfileDto> {
  return request<ProfileDto>('/profile');
}

export function updateBasics(basics: ResumeBasics): Promise<ResumeBasics> {
  return request<ResumeBasics>('/profile/basics', { method: 'PUT', body: json(basics) });
}

// ---------------------------------------------------------------------------
// Knowledge base
// ---------------------------------------------------------------------------

export function listKnowledge(): Promise<KnowledgeItemDto[]> {
  return request<KnowledgeItemDto[]>('/knowledge');
}

export function getKnowledgeItem(itemId: string): Promise<KnowledgeItemDetailDto> {
  return request<KnowledgeItemDetailDto>(`/knowledge/${id(itemId)}`);
}

export function upsertKnowledge(itemId: string, req: UpsertKnowledgeRequest): Promise<KnowledgeItemDetailDto> {
  return request<KnowledgeItemDetailDto>(`/knowledge/${id(itemId)}`, { method: 'PUT', body: json(req) });
}

export function deleteKnowledge(itemId: string): Promise<void> {
  return request<void>(`/knowledge/${id(itemId)}`, { method: 'DELETE' });
}

export function importGithub(req: GitHubImportRequest): Promise<GitHubImportResult> {
  return request<GitHubImportResult>('/knowledge/import/github', { method: 'POST', body: json(req) });
}

// ---------------------------------------------------------------------------
// Resumes
// ---------------------------------------------------------------------------

export function listResumes(): Promise<ResumeSummaryDto[]> {
  return request<ResumeSummaryDto[]>('/resumes');
}

export function getResume(resumeId: string): Promise<ResumeDocument> {
  return request<ResumeDocument>(`/resumes/${id(resumeId)}`);
}

export function rebuildBaseResume(): Promise<ResumeDocument> {
  return request<ResumeDocument>('/resumes/base', { method: 'POST' });
}

// ---------------------------------------------------------------------------
// Jobs
// ---------------------------------------------------------------------------

export function createJob(req: CreateJobRequest): Promise<JobPosting> {
  return request<JobPosting>('/jobs', { method: 'POST', body: json(req) });
}

export function getJobAnalysis(jobId: string): Promise<JobAnalysis> {
  return request<JobAnalysis>(`/jobs/${id(jobId)}/analysis`);
}

// ---------------------------------------------------------------------------
// Tailoring
// ---------------------------------------------------------------------------

export function runTailor(req: TailorRequest): Promise<TailoringResult> {
  return request<TailoringResult>('/tailor', { method: 'POST', body: json(req) });
}

export function getTailorTrace(runId: string): Promise<TailoringResult['trace']> {
  return request<TailoringResult['trace']>(`/tailor/${id(runId)}/trace`);
}

// ---------------------------------------------------------------------------
// Render
// ---------------------------------------------------------------------------

export function renderResume(resumeId: string, format: RenderFormat): Promise<Blob> {
  return requestBlob(`/render/${id(resumeId)}`, { method: 'POST', body: json({ format }) });
}

// ---------------------------------------------------------------------------
// Autofill (consumed by the browser extension; exposed here for completeness)
// ---------------------------------------------------------------------------

export function getAutofillProfile(): Promise<AutofillProfile> {
  return request<AutofillProfile>('/autofill/profile');
}

export function resolveAutofillFields(req: ResolveFieldsRequest): Promise<ResolveFieldsResponse> {
  return request<ResolveFieldsResponse>('/autofill/resolve', { method: 'POST', body: json(req) });
}

export function saveFieldMap(map: LearnedFieldMap): Promise<void> {
  return request<void>('/autofill/fieldmap', { method: 'POST', body: json(map) });
}

export async function getFieldMap(host: string): Promise<LearnedFieldMap | null> {
  try {
    return await request<LearnedFieldMap>(`/autofill/fieldmap/${id(host)}`);
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return null;
    }
    throw err;
  }
}

// ---------------------------------------------------------------------------
// Applications
// ---------------------------------------------------------------------------

export function listApplications(): Promise<ApplicationDto[]> {
  return request<ApplicationDto[]>('/applications');
}

export function createApplication(req: CreateApplicationRequest): Promise<ApplicationDto> {
  return request<ApplicationDto>('/applications', { method: 'POST', body: json(req) });
}

export function updateApplication(appId: string, req: UpdateApplicationRequest): Promise<ApplicationDto> {
  return request<ApplicationDto>(`/applications/${id(appId)}`, { method: 'PATCH', body: json(req) });
}
