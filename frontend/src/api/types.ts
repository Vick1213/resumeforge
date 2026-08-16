/**
 * Hand-written TypeScript mirrors of the DTOs defined in docs/CONTRACTS.md
 * (sections 2, 4, 5, 6, 7, 9, 10).
 *
 * This file exists because the backend is not running yet. Once the API is
 * up, replace it with generated types:
 *
 *   npm run generate:api
 *
 * which pulls the OpenAPI document at /openapi/v1.json and regenerates this
 * module, so contract drift fails the build instead of failing silently at
 * runtime. Until then, keep this file's shapes in lockstep with
 * docs/CONTRACTS.md by hand.
 *
 * Wire format: all JSON is camelCase. C# nullable properties are configured
 * with JsonIgnoreCondition.WhenWritingNull, meaning a null value is *omitted*
 * from the payload rather than sent as `null` — so nullable C# members are
 * modeled here as optional (`prop?:`) TypeScript members, not `T | null`.
 * Enums serialize as camelCase strings.
 */

// ---------------------------------------------------------------------------
// Shared scalar aliases
// ---------------------------------------------------------------------------

/** DateTimeOffset, ISO-8601 with offset, e.g. "2024-11-03T18:22:00Z". */
export type IsoDateTime = string;

/** DateOnly, "yyyy-MM-dd". */
export type IsoDate = string;

/**
 * TimeSpan, serialized by System.Text.Json's constant ("c") format, e.g.
 * "00:00:01.2345678". Use parseTimeSpanMs() in src/lib/time.ts to convert.
 */
export type TimeSpanString = string;

// ---------------------------------------------------------------------------
// RFC 9457 Problem Details (used by every error response)
// ---------------------------------------------------------------------------

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  [extension: string]: unknown;
}

// ---------------------------------------------------------------------------
// 2. Canonical resume model
// ---------------------------------------------------------------------------

export type SectionKind =
  | 'summary'
  | 'skills'
  | 'experience'
  | 'projects'
  | 'education'
  | 'certifications';

export interface ResumeBasics {
  fullName: string;
  headline?: string;
  email?: string;
  phone?: string;
  location?: string;
  website?: string;
  linkedIn?: string;
  gitHub?: string;
}

export interface Bullet {
  id: string;
  text: string;
  variants: string[];
  tags: string[];
  relevance: number;
}

export interface ExperienceEntry {
  id: string;
  role: string;
  organization: string;
  location?: string;
  startDate: IsoDate;
  endDate?: IsoDate;
  bullets: Bullet[];
  tech: string[];
  included: boolean;
}

export interface ProjectEntry {
  id: string;
  name: string;
  tagline?: string;
  url?: string;
  repoUrl?: string;
  startDate?: IsoDate;
  endDate?: IsoDate;
  bullets: Bullet[];
  tech: string[];
  included: boolean;
}

export interface EducationEntry {
  id: string;
  institution: string;
  credential: string;
  startDate?: IsoDate;
  endDate?: IsoDate;
  location?: string;
  gpa?: number;
  highlights: string[];
  included: boolean;
}

export interface CertificationEntry {
  id: string;
  name: string;
  issuer?: string;
  issuedOn?: IsoDate;
  credentialUrl?: string;
  included: boolean;
}

export interface Skill {
  id: string;
  name: string;
  normalized: string;
  emphasized: boolean;
}

export interface SkillGroup {
  id: string;
  label: string;
  items: Skill[];
  included: boolean;
}

export interface ResumeDocument {
  id: string;
  name: string;
  basics: ResumeBasics;
  summary?: string;
  skills: SkillGroup[];
  experience: ExperienceEntry[];
  projects: ProjectEntry[];
  education: EducationEntry[];
  certifications: CertificationEntry[];
  sectionOrder: SectionKind[];
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

// ---------------------------------------------------------------------------
// 3. Knowledge base (frontend-only DTOs for the markdown-backed KB)
// ---------------------------------------------------------------------------

export type KnowledgeItemKind = 'experience' | 'project' | 'education' | 'certification' | 'basics';

export type KnowledgeSource = 'manual' | 'github';

export type DiagnosticSeverity = 'info' | 'warning' | 'error';

export interface KnowledgeBaseDiagnostic {
  file: string;
  line: number;
  message: string;
  severity: DiagnosticSeverity;
}

export interface KnowledgeItemDto {
  id: string;
  kind: KnowledgeItemKind;
  title: string;
  subtitle?: string;
  tech: string[];
  tags: string[];
  source: KnowledgeSource;
  updatedAt: IsoDateTime;
}

export interface KnowledgeItemDetailDto extends KnowledgeItemDto {
  rawMarkdown: string;
  diagnostics: KnowledgeBaseDiagnostic[];
}

export interface UpsertKnowledgeRequest {
  rawMarkdown: string;
}

export interface GitHubImportRequest {
  username: string;
  /** Repo names to act on. Empty with commit=false lists everything available. */
  repos: string[];
  commit: boolean;
}

export interface GitHubRepoPreview {
  repoName: string;
  description?: string;
  language?: string;
  stars: number;
  url: string;
  generatedMarkdown: string;
}

export interface GitHubImportResult {
  repos: GitHubRepoPreview[];
  committed: string[];
}

// ---------------------------------------------------------------------------
// Knowledge counts + profile (used by /api/profile)
// ---------------------------------------------------------------------------

export interface KnowledgeCounts {
  experience: number;
  projects: number;
  education: number;
  certifications: number;
  skills: number;
}

export interface ProfileDto {
  basics: ResumeBasics;
  counts: KnowledgeCounts;
  diagnostics: KnowledgeBaseDiagnostic[];
  hasApiKey: boolean;
  model: string;
}

// ---------------------------------------------------------------------------
// Resumes
// ---------------------------------------------------------------------------

export interface ResumeSummaryDto {
  id: string;
  name: string;
  isBase: boolean;
  updatedAt: IsoDateTime;
}

// ---------------------------------------------------------------------------
// 4. Job description analysis
// ---------------------------------------------------------------------------

export interface JobPosting {
  id: string;
  sourceUrl: string;
  company?: string;
  title?: string;
  location?: string;
  rawText: string;
  fetchedAt: IsoDateTime;
}

export type SeniorityLevel = 'unknown' | 'intern' | 'junior' | 'mid' | 'senior' | 'staff' | 'principal';

export type RequirementKind = 'skill' | 'experience' | 'education' | 'responsibility' | 'other';

export interface Requirement {
  id: string;
  text: string;
  kind: RequirementKind;
  isMandatory: boolean;
  skills: string[];
  weight: number;
}

export interface JobAnalysis {
  jobId: string;
  requirements: Requirement[];
  keywords: string[];
  matchedSkills: string[];
  missingSkills: string[];
  seniority: SeniorityLevel;
}

export interface CreateJobRequest {
  url?: string;
  rawText?: string;
}

// ---------------------------------------------------------------------------
// 5. Relevance scoring
// ---------------------------------------------------------------------------

export interface ScoredCandidate {
  entityId: string;
  text: string;
  score: number;
  matchedRequirements: string[];
}

export interface CandidateSet {
  experience: ScoredCandidate[];
  projects: ScoredCandidate[];
  skills: ScoredCandidate[];
}

// ---------------------------------------------------------------------------
// 6. Tailoring commands
// ---------------------------------------------------------------------------

interface TailorCommandBase {
  rationale?: string;
}

export interface IncludeCommand extends TailorCommandBase {
  op: 'include';
  targets: string[];
}

export interface ExcludeCommand extends TailorCommandBase {
  op: 'exclude';
  targets: string[];
}

export interface OrderCommand extends TailorCommandBase {
  op: 'order';
  parent: string;
  order: string[];
}

export interface SelectVariantCommand extends TailorCommandBase {
  op: 'selectVariant';
  target: string;
  variantIndex: number;
}

export interface RewriteCommand extends TailorCommandBase {
  op: 'rewrite';
  target: string;
  text: string;
}

export interface SetSummaryCommand extends TailorCommandBase {
  op: 'setSummary';
  text: string;
}

export interface EmphasizeSkillsCommand extends TailorCommandBase {
  op: 'emphasizeSkills';
  skills: string[];
}

export interface SetSectionOrderCommand extends TailorCommandBase {
  op: 'setSectionOrder';
  order: SectionKind[];
}

/** Discriminated union on `op`, mirroring the [JsonPolymorphic] hierarchy. */
export type TailorCommand =
  | IncludeCommand
  | ExcludeCommand
  | OrderCommand
  | SelectVariantCommand
  | RewriteCommand
  | SetSummaryCommand
  | EmphasizeSkillsCommand
  | SetSectionOrderCommand;

export type TailorCommandOp = TailorCommand['op'];

// The `string & {}` branch keeps the named codes visible for autocomplete
// while still accepting any code the server might add later.
export type RejectionCode =
  | 'unknown-target'
  | 'unknown-parent'
  | 'variant-index-out-of-range'
  | 'rewrite-too-long'
  | 'rewrite-multiline'
  | 'fabricated-metric'
  | 'rewrite-budget-exceeded'
  | 'duplicate-order-entry'
  | (string & {});

export interface RejectedCommand {
  command: TailorCommand;
  reason: string;
  code: RejectionCode;
}

export interface CommandValidationResult {
  accepted: TailorCommand[];
  rejected: RejectedCommand[];
}

export type DiffKind =
  | 'included'
  | 'excluded'
  | 'reordered'
  | 'rewritten'
  | 'variantSelected'
  | 'summarySet'
  | 'skillEmphasized';

export interface ResumeDiffEntry {
  entityId: string;
  kind: DiffKind;
  before?: string;
  after?: string;
  rationale?: string;
}

export interface RequirementCoverage {
  requirementId: string;
  requirementText: string;
  covered: boolean;
  evidenceIds: string[];
}

export interface CoverageReport {
  score: number;
  requirements: RequirementCoverage[];
}

export interface TokenUsage {
  inputTokens: number;
  outputTokens: number;
  modelCalls: number;
  cacheHits: number;
}

// ---------------------------------------------------------------------------
// 7. Graph engine trace
// ---------------------------------------------------------------------------

export type GraphNodeStatus = 'succeeded' | 'failed' | 'skipped' | 'cancelled';

export interface GraphNodeTrace {
  node: string;
  status: GraphNodeStatus;
  duration: TimeSpanString;
  error?: string;
  inputTokens: number;
  outputTokens: number;
}

export interface TailoringResult {
  document: ResumeDocument;
  diff: ResumeDiffEntry[];
  commands: CommandValidationResult;
  coverage: CoverageReport;
  usage: TokenUsage;
  trace: GraphNodeTrace[];
}

// ---------------------------------------------------------------------------
// 9. HTTP API request/response DTOs
// ---------------------------------------------------------------------------

export interface TailorRequest {
  jobId: string;
  baseResumeId?: string;
  maxRewrites: number;
  dryRun: boolean;
}

export type RenderFormat = 'pdf' | 'html' | 'md' | 'docx';

export interface RenderRequest {
  format: RenderFormat;
}

export type ApplicationStatus = 'saved' | 'applied' | 'screening' | 'interview' | 'offer' | 'rejected' | 'withdrawn';

export interface ApplicationDto {
  id: string;
  jobId: string;
  resumeId?: string;
  company: string;
  title: string;
  status: ApplicationStatus;
  jobUrl?: string;
  location?: string;
  notes?: string;
  coverageScore?: number;
  /** Token spend of the tailoring run that produced resumeId, if any. */
  usage?: TokenUsage;
  appliedAt?: IsoDateTime;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

export interface CreateApplicationRequest {
  jobId: string;
  resumeId?: string;
  company: string;
  title: string;
  status?: ApplicationStatus;
  jobUrl?: string;
  location?: string;
  notes?: string;
  coverageScore?: number;
  usage?: TokenUsage;
}

export interface UpdateApplicationRequest {
  status?: ApplicationStatus;
  notes?: string;
  appliedAt?: IsoDateTime;
}

// ---------------------------------------------------------------------------
// 10. Autofill contracts
// ---------------------------------------------------------------------------

export type CanonicalFieldKey =
  | 'firstName'
  | 'lastName'
  | 'fullName'
  | 'preferredName'
  | 'email'
  | 'phone'
  | 'addressLine1'
  | 'addressLine2'
  | 'city'
  | 'state'
  | 'postalCode'
  | 'country'
  | 'linkedin'
  | 'github'
  | 'portfolio'
  | 'website'
  | 'currentCompany'
  | 'currentTitle'
  | 'yearsExperience'
  | 'workAuthorization'
  | 'requiresSponsorship'
  | 'willingToRelocate'
  | 'noticePeriod'
  | 'desiredSalary'
  | 'availableStartDate'
  | 'gender'
  | 'ethnicity'
  | 'veteranStatus'
  | 'disabilityStatus'
  | 'howDidYouHear'
  | 'referredBy';

export interface AutofillDocument {
  kind: 'resume' | 'coverLetter';
  fileName: string;
  downloadUrl: string;
}

export interface AutofillProfile {
  fields: Partial<Record<CanonicalFieldKey, string | undefined>>;
  documents: AutofillDocument[];
}

export type AutofillInputType =
  | 'text'
  | 'email'
  | 'tel'
  | 'select'
  | 'radio'
  | 'checkbox'
  | 'file'
  | 'textarea';

export interface UnresolvedField {
  elementId: string;
  label?: string;
  name?: string;
  placeholder?: string;
  autoComplete?: string;
  inputType: AutofillInputType;
  options: string[];
}

export interface ResolveFieldsRequest {
  host: string;
  formSignature: string;
  fields: UnresolvedField[];
}

export interface FieldResolution {
  elementId: string;
  canonicalKey: CanonicalFieldKey | '';
  confidence: number;
  optionValue?: string;
}

export interface ResolveFieldsResponse {
  resolutions: FieldResolution[];
  usage: TokenUsage;
}

export interface LearnedFieldMap {
  host: string;
  formSignature: string;
  elementToKey: Record<string, string>;
  learnedAt: IsoDateTime;
  hitCount: number;
}
