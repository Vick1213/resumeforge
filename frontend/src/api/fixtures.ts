import type {
  ApplicationDto,
  AtsReview,
  AutofillProfile,
  CoverageReport,
  GitHubRepoPreview,
  GraphNodeTrace,
  JobAnalysis,
  JobPosting,
  KnowledgeItemDetailDto,
  KnowledgeItemDto,
  ProfileDto,
  ResumeDiffEntry,
  ResumeDocument,
  ResumeSummaryDto,
  TailorCommand,
  TailoringResult,
} from '@/api/types';

const NOW = new Date('2026-08-15T16:00:00Z');
const isoDaysAgo = (days: number): string => new Date(NOW.getTime() - days * 86_400_000).toISOString();

// ---------------------------------------------------------------------------
// Resume document (base resume)
// ---------------------------------------------------------------------------

export const fixtureBaseResume: ResumeDocument = {
  id: 'resume-base',
  name: 'Base resume',
  basics: {
    fullName: 'Jordan Avery',
    headline: 'Senior Backend Engineer',
    email: 'jordan.avery@example.com',
    phone: '+1 (206) 555-0142',
    location: 'Seattle, WA',
    website: 'https://jordanavery.dev',
    linkedIn: 'https://linkedin.com/in/jordanavery',
    gitHub: 'https://github.com/jordanavery',
  },
  summary:
    'Backend engineer with 7 years building distributed systems in C# and Go. Focused on latency, ' +
    'observability, and teams that ship without drama.',
  skills: [
    {
      id: 'skl:languages',
      label: 'Languages',
      included: true,
      items: [
        { id: 'skl:languages#csharp', name: 'C#', normalized: 'csharp', emphasized: true },
        { id: 'skl:languages#typescript', name: 'TypeScript', normalized: 'typescript', emphasized: true },
        { id: 'skl:languages#go', name: 'Go', normalized: 'go', emphasized: false },
        { id: 'skl:languages#python', name: 'Python', normalized: 'python', emphasized: false },
      ],
    },
    {
      id: 'skl:cloud',
      label: 'Cloud & Infra',
      included: true,
      items: [
        { id: 'skl:cloud#kubernetes', name: 'Kubernetes', normalized: 'kubernetes', emphasized: true },
        { id: 'skl:cloud#azure', name: 'Azure', normalized: 'azure', emphasized: false },
        { id: 'skl:cloud#postgresql', name: 'PostgreSQL', normalized: 'postgresql', emphasized: true },
        { id: 'skl:cloud#terraform', name: 'Terraform', normalized: 'terraform', emphasized: false },
      ],
    },
  ],
  experience: [
    {
      id: 'exp:acme-corp',
      role: 'Senior Software Engineer',
      organization: 'Acme Corp',
      location: 'Seattle, WA',
      startDate: '2022-03-01',
      included: true,
      tech: ['C#', '.NET', 'PostgreSQL', 'Kubernetes'],
      bullets: [
        {
          id: 'exp:acme-corp#0',
          text: 'Cut p99 checkout latency from 840ms to 120ms by replacing a serial fan-out with a bounded parallel scatter-gather over 6 downstream services.',
          variants: [
            'Rebuilt checkout fan-out as a bounded scatter-gather, cutting p99 from 840ms to 120ms.',
          ],
          tags: ['performance', 'distributed-systems'],
          relevance: 0.91,
        },
        {
          id: 'exp:acme-corp#1',
          text: 'Led migration of 40+ services from .NET 6 to .NET 8, retiring 12k lines of shim code.',
          variants: [],
          tags: ['backend', 'migration'],
          relevance: 0.64,
        },
      ],
    },
    {
      id: 'exp:nimbus-systems',
      role: 'Software Engineer',
      organization: 'Nimbus Systems',
      location: 'Portland, OR',
      startDate: '2019-06-01',
      endDate: '2022-02-28',
      included: true,
      tech: ['Go', 'gRPC', 'Kafka'],
      bullets: [
        {
          id: 'exp:nimbus-systems#0',
          text: 'Designed an event-sourced billing pipeline processing 2M events/day with exactly-once semantics.',
          variants: [
            'Built an event-sourced billing pipeline handling 2M events/day, exactly-once delivery.',
          ],
          tags: ['event-sourcing', 'billing'],
          relevance: 0.58,
        },
        {
          id: 'exp:nimbus-systems#1',
          text: 'Introduced structured logging and distributed tracing, cutting mean incident triage time by 65%.',
          variants: [],
          tags: ['observability'],
          relevance: 0.47,
        },
      ],
    },
  ],
  projects: [
    {
      id: 'prj:graph-runner',
      name: 'Graph Runner',
      tagline: 'A DAG executor for multi-agent pipelines',
      url: 'https://example.com/graph-runner',
      repoUrl: 'https://github.com/jordanavery/graph-runner',
      startDate: '2024-01-01',
      included: true,
      tech: ['TypeScript', 'Node.js'],
      bullets: [
        {
          id: 'prj:graph-runner#0',
          text: 'Built a scheduler that streams work items through stages without a barrier, cutting wall-clock time 3x versus a staged design.',
          variants: [],
          tags: ['orchestration', 'concurrency'],
          relevance: 0.72,
        },
      ],
    },
  ],
  education: [
    {
      id: 'edu:uw-madison',
      institution: 'University of Wisconsin–Madison',
      credential: 'B.S. Computer Science',
      startDate: '2015-09-01',
      endDate: '2019-05-01',
      location: 'Madison, WI',
      included: true,
      highlights: [],
    },
  ],
  certifications: [
    {
      id: 'cert:az-204',
      name: 'Azure Developer Associate',
      issuer: 'Microsoft',
      issuedOn: '2023-04-01',
      included: true,
    },
  ],
  sectionOrder: ['summary', 'skills', 'experience', 'projects', 'education', 'certifications'],
  createdAt: isoDaysAgo(220),
  updatedAt: isoDaysAgo(3),
};

export const fixtureResumeSummaries: ResumeSummaryDto[] = [
  { id: 'resume-base', name: 'Base resume', isBase: true, updatedAt: isoDaysAgo(3) },
  { id: 'resume-vertex', name: 'Vertex Systems — Backend Eng', isBase: false, updatedAt: isoDaysAgo(1) },
];

// ---------------------------------------------------------------------------
// Profile / knowledge base
// ---------------------------------------------------------------------------

export const fixtureProfile: ProfileDto = {
  basics: fixtureBaseResume.basics,
  counts: { experience: 2, projects: 1, education: 1, certifications: 1, skills: 2 },
  diagnostics: [
    {
      file: 'profile/experience/legacy-co.md',
      line: 4,
      message: 'startDate "22-2019" is not yyyy-MM, yyyy-MM-dd, yyyy, or "present"; file skipped.',
      severity: 'warning',
    },
  ],
  hasApiKey: false,
  model: 'claude-sonnet-5',
};

export const fixtureKnowledgeItems: KnowledgeItemDto[] = [
  {
    id: 'exp:acme-corp',
    kind: 'experience',
    title: 'Senior Software Engineer',
    subtitle: 'Acme Corp',
    tech: ['C#', '.NET', 'PostgreSQL', 'Kubernetes'],
    tags: ['backend', 'distributed-systems', 'performance'],
    source: 'manual',
    updatedAt: isoDaysAgo(3),
  },
  {
    id: 'exp:nimbus-systems',
    kind: 'experience',
    title: 'Software Engineer',
    subtitle: 'Nimbus Systems',
    tech: ['Go', 'gRPC', 'Kafka'],
    tags: ['event-sourcing', 'observability'],
    source: 'manual',
    updatedAt: isoDaysAgo(40),
  },
  {
    id: 'prj:graph-runner',
    kind: 'project',
    title: 'Graph Runner',
    subtitle: 'A DAG executor for multi-agent pipelines',
    tech: ['TypeScript', 'Node.js'],
    tags: ['orchestration', 'concurrency'],
    source: 'github',
    updatedAt: isoDaysAgo(12),
  },
  {
    id: 'edu:uw-madison',
    kind: 'education',
    title: 'B.S. Computer Science',
    subtitle: 'University of Wisconsin–Madison',
    tech: [],
    tags: [],
    source: 'manual',
    updatedAt: isoDaysAgo(200),
  },
  {
    id: 'cert:az-204',
    kind: 'certification',
    title: 'Azure Developer Associate',
    subtitle: 'Microsoft',
    tech: [],
    tags: [],
    source: 'manual',
    updatedAt: isoDaysAgo(150),
  },
];

const ACME_MARKDOWN = `---
type: experience
role: Senior Software Engineer
organization: Acme Corp
location: Seattle, WA
startDate: 2022-03
tech: [C#, .NET, PostgreSQL, Kubernetes]
tags: [backend, distributed-systems, performance]
---

- Cut p99 checkout latency from 840ms to 120ms by replacing a serial fan-out with a
  bounded parallel scatter-gather over 6 downstream services.
  - Rebuilt checkout fan-out as a bounded scatter-gather, cutting p99 from 840ms to 120ms.
- Led migration of 40+ services from .NET 6 to .NET 8, retiring 12k lines of shim code.
`;

const GRAPH_RUNNER_MARKDOWN = `---
type: project
name: Graph Runner
tagline: A DAG executor for multi-agent pipelines
url: https://example.com/graph-runner
repoUrl: https://github.com/jordanavery/graph-runner
startDate: 2024-01
endDate: present
tech: [TypeScript, Node.js]
tags: [orchestration, concurrency]
source: github
stars: 412
---

- Built a scheduler that streams work items through stages without a barrier, cutting
  wall-clock time 3x versus a staged design.
`;

export const fixtureKnowledgeDetails: Record<string, KnowledgeItemDetailDto> = {
  'exp:acme-corp': {
    ...fixtureKnowledgeItems[0]!,
    rawMarkdown: ACME_MARKDOWN,
    diagnostics: [],
  },
  'prj:graph-runner': {
    ...fixtureKnowledgeItems[2]!,
    rawMarkdown: GRAPH_RUNNER_MARKDOWN,
    diagnostics: [],
  },
};

/** Generates a plausible markdown stub for a knowledge item that has no hand-authored fixture body. */
export function stubMarkdownFor(item: KnowledgeItemDto): string {
  switch (item.kind) {
    case 'experience':
      return `---\ntype: experience\nrole: ${item.title}\norganization: ${item.subtitle ?? 'Unknown'}\nstartDate: 2021-01\ntech: [${item.tech.join(', ')}]\ntags: [${item.tags.join(', ')}]\n---\n\n- ${item.title} at ${item.subtitle ?? 'Unknown'}.\n`;
    case 'project':
      return `---\ntype: project\nname: ${item.title}\ntagline: ${item.subtitle ?? ''}\ntech: [${item.tech.join(', ')}]\ntags: [${item.tags.join(', ')}]\nsource: ${item.source}\n---\n\n- ${item.subtitle ?? item.title}.\n`;
    case 'education':
      return `---\ntype: education\ninstitution: ${item.subtitle ?? 'Unknown'}\ncredential: ${item.title}\n---\n\n`;
    case 'certification':
      return `---\ntype: certification\nname: ${item.title}\nissuer: ${item.subtitle ?? ''}\n---\n\n`;
    case 'basics':
    default:
      return `---\ntype: basics\nfullName: ${item.title}\n---\n\n`;
  }
}

/** Seeds a full detail record for a knowledge list item, preferring a hand-authored fixture body. */
export function seedKnowledgeDetail(item: KnowledgeItemDto): KnowledgeItemDetailDto {
  const existing = fixtureKnowledgeDetails[item.id];
  if (existing) return existing;
  return { ...item, rawMarkdown: stubMarkdownFor(item), diagnostics: [] };
}

export const fixtureGithubRepos: GitHubRepoPreview[] = [
  {
    repoName: 'graph-runner',
    description: 'A DAG executor for multi-agent pipelines',
    language: 'TypeScript',
    stars: 412,
    url: 'https://github.com/jordanavery/graph-runner',
    generatedMarkdown: GRAPH_RUNNER_MARKDOWN,
  },
  {
    repoName: 'bm25-lite',
    description: 'A minimal BM25 ranking implementation with zero dependencies',
    language: 'TypeScript',
    stars: 58,
    url: 'https://github.com/jordanavery/bm25-lite',
    generatedMarkdown: `---\ntype: project\nname: BM25 Lite\ntagline: A minimal BM25 ranking implementation with zero dependencies\nurl: \nrepoUrl: https://github.com/jordanavery/bm25-lite\ntech: [TypeScript]\ntags: [search, ranking]\nsource: github\nstars: 58\n---\n\n- Implemented BM25 scoring from the paper with no runtime dependencies, published as a 4kb package.\n`,
  },
  {
    repoName: 'dotfiles',
    description: 'Personal shell configuration',
    language: 'Shell',
    stars: 3,
    url: 'https://github.com/jordanavery/dotfiles',
    generatedMarkdown: `---\ntype: project\nname: Dotfiles\ntagline: Personal shell configuration\nrepoUrl: https://github.com/jordanavery/dotfiles\ntech: [Shell]\ntags: []\nsource: github\nstars: 3\n---\n\n- Personal zsh and neovim configuration.\n`,
  },
];

// ---------------------------------------------------------------------------
// Job posting + analysis
// ---------------------------------------------------------------------------

export const fixtureJobPosting: JobPosting = {
  id: 'job-vertex-backend',
  sourceUrl: 'https://boards.greenhouse.io/vertexsystems/jobs/5821034',
  company: 'Vertex Systems',
  title: 'Senior Backend Engineer',
  location: 'Remote (US)',
  rawText:
    'Vertex Systems is looking for a Senior Backend Engineer to own our checkout platform...\n\n' +
    'Requirements:\n- 5+ years building production services in C# or Go\n' +
    '- Deep experience with distributed systems and performance optimization\n' +
    '- Hands-on Kubernetes and PostgreSQL experience\n' +
    '- Experience with event-driven architectures\n' +
    'Nice to have:\n- Experience with gRPC\n- Prior startup experience\n',
  fetchedAt: isoDaysAgo(1),
};

export const fixtureJobAnalysis: JobAnalysis = {
  jobId: 'job-vertex-backend',
  seniority: 'senior',
  keywords: ['distributed systems', 'kubernetes', 'postgresql', 'grpc', 'event-driven', 'performance'],
  matchedSkills: ['C#', 'Kubernetes', 'PostgreSQL', 'Go', 'gRPC'],
  missingSkills: ['Terraform (production)'],
  requirements: [
    {
      id: 'req:0',
      text: '5+ years building production services in C# or Go',
      kind: 'experience',
      isMandatory: true,
      skills: ['C#', 'Go'],
      weight: 0.95,
    },
    {
      id: 'req:1',
      text: 'Deep experience with distributed systems and performance optimization',
      kind: 'skill',
      isMandatory: true,
      skills: ['distributed-systems', 'performance'],
      weight: 0.9,
    },
    {
      id: 'req:2',
      text: 'Hands-on Kubernetes and PostgreSQL experience',
      kind: 'skill',
      isMandatory: true,
      skills: ['Kubernetes', 'PostgreSQL'],
      weight: 0.85,
    },
    {
      id: 'req:3',
      text: 'Experience with event-driven architectures',
      kind: 'responsibility',
      isMandatory: true,
      skills: ['event-sourcing', 'kafka'],
      weight: 0.7,
    },
    {
      id: 'req:4',
      text: 'Experience with gRPC',
      kind: 'skill',
      isMandatory: false,
      skills: ['gRPC'],
      weight: 0.4,
    },
    {
      id: 'req:5',
      text: 'Prior startup experience',
      kind: 'other',
      isMandatory: false,
      skills: [],
      weight: 0.2,
    },
  ],
};

// ---------------------------------------------------------------------------
// Tailoring run: graph trace, diff, coverage, commands, usage
// ---------------------------------------------------------------------------

export const fixtureGraphTrace: GraphNodeTrace[] = [
  { node: 'fetch-jd', status: 'succeeded', duration: '00:00:00.3120000', inputTokens: 0, outputTokens: 0 },
  { node: 'load-kb', status: 'succeeded', duration: '00:00:00.0410000', inputTokens: 0, outputTokens: 0 },
  { node: 'analyze-jd', status: 'succeeded', duration: '00:00:00.1870000', inputTokens: 0, outputTokens: 0 },
  { node: 'build-base', status: 'succeeded', duration: '00:00:00.0650000', inputTokens: 0, outputTokens: 0 },
  { node: 'score-experience', status: 'succeeded', duration: '00:00:00.0920000', inputTokens: 0, outputTokens: 0 },
  { node: 'score-projects', status: 'succeeded', duration: '00:00:00.0740000', inputTokens: 0, outputTokens: 0 },
  { node: 'score-skills', status: 'succeeded', duration: '00:00:00.0510000', inputTokens: 0, outputTokens: 0 },
  { node: 'build-brief', status: 'succeeded', duration: '00:00:00.0280000', inputTokens: 0, outputTokens: 0 },
  {
    node: 'propose-commands',
    status: 'succeeded',
    duration: '00:00:02.4100000',
    inputTokens: 812,
    outputTokens: 244,
  },
  { node: 'validate-commands', status: 'succeeded', duration: '00:00:00.0190000', inputTokens: 0, outputTokens: 0 },
  {
    node: 'verify-fabrication',
    status: 'succeeded',
    duration: '00:00:00.0120000',
    inputTokens: 0,
    outputTokens: 0,
  },
  { node: 'verify-coverage', status: 'succeeded', duration: '00:00:00.0150000', inputTokens: 0, outputTokens: 0 },
  { node: 'execute-commands', status: 'succeeded', duration: '00:00:00.0080000', inputTokens: 0, outputTokens: 0 },
  { node: 'render', status: 'succeeded', duration: '00:00:00.1440000', inputTokens: 0, outputTokens: 0 },
];

export const fixtureCommands: TailorCommand[] = [
  { op: 'include', targets: ['exp:acme-corp#0', 'exp:nimbus-systems#0'], rationale: 'Directly evidences distributed-systems and event-driven requirements' },
  { op: 'selectVariant', target: 'exp:acme-corp#0', variantIndex: 0, rationale: 'Shorter phrasing fits one line at this seniority' },
  { op: 'exclude', targets: ['exp:nimbus-systems#1'], rationale: 'Observability bullet is weaker match than billing pipeline' },
  { op: 'order', parent: 'root', order: ['exp:acme-corp', 'exp:nimbus-systems', 'prj:graph-runner'], rationale: 'Most relevant experience first' },
  {
    op: 'setSummary',
    text: 'Backend engineer with 7 years building distributed, event-driven systems in C# and Go, focused on latency and reliability at scale.',
    rationale: 'Leads with distributed systems and event-driven, the two mandatory requirements',
  },
  { op: 'emphasizeSkills', skills: ['csharp', 'kubernetes', 'postgresql'], rationale: 'Matches the three mandatory skill requirements' },
  {
    op: 'injectKeywords',
    target: 'exp:acme-corp#1',
    keywords: ['kubernetes', 'distributed systems'],
    text: 'Led migration of 40+ services from .NET 6 to .NET 8 on a Kubernetes-based distributed systems platform, retiring 12k lines of shim code.',
    rationale: "Surfaces two mandatory keywords already evidenced by this entry's tech and tags",
  },
];

export const fixtureRejectedCommands: TailoringResult['commands']['rejected'] = [
  {
    command: {
      op: 'rewrite',
      target: 'exp:nimbus-systems#0',
      text: 'Built an event-sourced billing pipeline processing 4M events/day with zero downtime.',
      rationale: 'Emphasize scale',
    },
    reason: 'Rewrite introduces the number "4M", which does not appear in the original bullet or its variants.',
    code: 'fabricated-metric',
  },
  {
    command: { op: 'include', targets: ['exp:ghost-employer#0'], rationale: 'Fill out timeline' },
    reason: 'Target "exp:ghost-employer#0" does not resolve to any node in the document.',
    code: 'unknown-target',
  },
  {
    command: {
      op: 'injectKeywords',
      target: 'exp:nimbus-systems#0',
      keywords: ['rust'],
      text: 'Designed an event-sourced billing pipeline in Rust processing 2M events/day with exactly-once semantics.',
      rationale: 'Requirement lists Rust as a nice-to-have',
    },
    reason: 'InjectKeywords text is 118 characters, which wraps onto 2 lines and leaves the last 11 % full. Write it to 1-108 characters or 169-210 characters.',
    code: 'ragged-line-fill',
  },
];

export const fixtureDiff: ResumeDiffEntry[] = [
  {
    entityId: 'exp:acme-corp#0',
    kind: 'variantSelected',
    before: 'Cut p99 checkout latency from 840ms to 120ms by replacing a serial fan-out with a bounded parallel scatter-gather over 6 downstream services.',
    after: 'Rebuilt checkout fan-out as a bounded scatter-gather, cutting p99 from 840ms to 120ms.',
    rationale: 'Shorter phrasing fits one line at this seniority',
  },
  {
    entityId: 'exp:nimbus-systems#1',
    kind: 'excluded',
    before: 'Introduced structured logging and distributed tracing, cutting mean incident triage time by 65%.',
    rationale: 'Observability bullet is weaker match than billing pipeline',
  },
  {
    entityId: 'root',
    kind: 'reordered',
    rationale: 'Most relevant experience first',
  },
  {
    entityId: 'sum',
    kind: 'summarySet',
    before: 'Backend engineer with 7 years building distributed systems in C# and Go. Focused on latency, observability, and teams that ship without drama.',
    after: 'Backend engineer with 7 years building distributed, event-driven systems in C# and Go, focused on latency and reliability at scale.',
    rationale: 'Leads with distributed systems and event-driven, the two mandatory requirements',
  },
  {
    entityId: 'skl:languages#csharp',
    kind: 'skillEmphasized',
    rationale: 'Matches the three mandatory skill requirements',
  },
  {
    entityId: 'exp:acme-corp#1',
    kind: 'keywordsInjected',
    before: 'Led migration of 40+ services from .NET 6 to .NET 8, retiring 12k lines of shim code.',
    after: 'Led migration of 40+ services from .NET 6 to .NET 8 on a Kubernetes-based distributed systems platform, retiring 12k lines of shim code.',
    keywords: ['kubernetes', 'distributed systems'],
    rationale: "Surfaces two mandatory keywords already evidenced by this entry's tech and tags",
  },
];

export const fixtureCoverage: CoverageReport = {
  score: 0.83,
  requirements: [
    { requirementId: 'req:0', requirementText: '5+ years building production services in C# or Go', covered: true, evidenceIds: ['exp:acme-corp', 'exp:nimbus-systems'] },
    { requirementId: 'req:1', requirementText: 'Deep experience with distributed systems and performance optimization', covered: true, evidenceIds: ['exp:acme-corp#0'] },
    { requirementId: 'req:2', requirementText: 'Hands-on Kubernetes and PostgreSQL experience', covered: true, evidenceIds: ['skl:cloud#kubernetes', 'skl:cloud#postgresql'] },
    { requirementId: 'req:3', requirementText: 'Experience with event-driven architectures', covered: true, evidenceIds: ['exp:nimbus-systems#0'] },
    { requirementId: 'req:4', requirementText: 'Experience with gRPC', covered: false, evidenceIds: [] },
    { requirementId: 'req:5', requirementText: 'Prior startup experience', covered: false, evidenceIds: [] },
  ],
};

const fixtureAtsReview: AtsReview = {
  scoreBefore: 61,
  scoreAfter: 84,
  verdict:
    "Strong distributed-systems evidence, but two of the posting's core terms never appear outside the skills list.",
  gaps: [
    {
      keyword: 'Kubernetes',
      importance: 'critical',
      skillsOnly: true,
      placement: 'exp:nimbus#1',
      angle:
        'The .NET 6 to .NET 8 migration across 40+ services ran on Kubernetes — name it in that bullet, alongside the 12,000 lines of shim code already there.',
    },
    {
      keyword: 'Terraform',
      importance: 'important',
      skillsOnly: false,
      placement: null,
      angle: 'Nothing in the resume evidences infrastructure-as-code; leave this gap open rather than claiming it.',
    },
  ],
  recruiterNotes: [
    'Kubernetes appears only in the skills list. A parser accepts that; a recruiter reads it as a claim with nothing behind it.',
  ],
};

export const fixtureTailoringResult: TailoringResult = {
  document: {
    ...fixtureBaseResume,
    id: 'resume-vertex',
    name: 'Vertex Systems — Backend Eng',
    summary: fixtureDiff.find((d) => d.entityId === 'sum')?.after ?? fixtureBaseResume.summary,
  },
  diff: fixtureDiff,
  commands: { accepted: fixtureCommands, rejected: fixtureRejectedCommands },
  coverage: fixtureCoverage,
  atsReview: fixtureAtsReview,
  usage: { inputTokens: 2140, outputTokens: 512, modelCalls: 2, cacheHits: 0 },
  trace: fixtureGraphTrace,
};

// ---------------------------------------------------------------------------
// Applications
// ---------------------------------------------------------------------------

export const fixtureApplications: ApplicationDto[] = [
  {
    id: 'app-1',
    jobId: 'job-vertex-backend',
    resumeId: 'resume-vertex',
    company: 'Vertex Systems',
    title: 'Senior Backend Engineer',
    status: 'applied',
    jobUrl: fixtureJobPosting.sourceUrl,
    location: 'Remote (US)',
    coverageScore: 0.83,
    usage: { inputTokens: 812, outputTokens: 244, modelCalls: 1, cacheHits: 0 },
    appliedAt: isoDaysAgo(1),
    createdAt: isoDaysAgo(2),
    updatedAt: isoDaysAgo(1),
  },
  {
    id: 'app-2',
    jobId: 'job-northwind-platform',
    resumeId: 'resume-base',
    company: 'Northwind Cloud',
    title: 'Platform Engineer',
    status: 'screening',
    location: 'Seattle, WA',
    coverageScore: 0.71,
    usage: { inputTokens: 764, outputTokens: 198, modelCalls: 1, cacheHits: 0 },
    appliedAt: isoDaysAgo(6),
    createdAt: isoDaysAgo(8),
    updatedAt: isoDaysAgo(4),
  },
  {
    id: 'app-3',
    jobId: 'job-fenwick-staff',
    resumeId: 'resume-base',
    company: 'Fenwick Labs',
    title: 'Staff Engineer, Infra',
    status: 'interview',
    location: 'Remote',
    coverageScore: 0.68,
    usage: { inputTokens: 891, outputTokens: 271, modelCalls: 1, cacheHits: 1 },
    appliedAt: isoDaysAgo(14),
    createdAt: isoDaysAgo(16),
    updatedAt: isoDaysAgo(2),
  },
  {
    id: 'app-4',
    jobId: 'job-halberd-backend',
    resumeId: 'resume-base',
    company: 'Halberd Systems',
    title: 'Backend Engineer',
    status: 'saved',
    location: 'Austin, TX',
    createdAt: isoDaysAgo(1),
    updatedAt: isoDaysAgo(1),
  },
  {
    id: 'app-5',
    jobId: 'job-orbital-senior',
    resumeId: 'resume-base',
    company: 'Orbital Data',
    title: 'Senior Software Engineer',
    status: 'offer',
    location: 'Remote (US)',
    coverageScore: 0.88,
    appliedAt: isoDaysAgo(30),
    createdAt: isoDaysAgo(33),
    updatedAt: isoDaysAgo(1),
  },
  {
    id: 'app-6',
    jobId: 'job-crestline-backend',
    resumeId: 'resume-base',
    company: 'Crestline',
    title: 'Backend Engineer II',
    status: 'rejected',
    location: 'Denver, CO',
    coverageScore: 0.54,
    appliedAt: isoDaysAgo(45),
    createdAt: isoDaysAgo(47),
    updatedAt: isoDaysAgo(20),
  },
];

// ---------------------------------------------------------------------------
// Autofill (used by the browser extension; included for client completeness)
// ---------------------------------------------------------------------------

export const fixtureAutofillProfile: AutofillProfile = {
  fields: {
    firstName: 'Jordan',
    lastName: 'Avery',
    fullName: 'Jordan Avery',
    email: 'jordan.avery@example.com',
    phone: '+1 (206) 555-0142',
    city: 'Seattle',
    state: 'WA',
    country: 'United States',
    linkedin: 'https://linkedin.com/in/jordanavery',
    github: 'https://github.com/jordanavery',
    website: 'https://jordanavery.dev',
    currentCompany: 'Acme Corp',
    currentTitle: 'Senior Software Engineer',
    yearsExperience: '7',
    workAuthorization: 'US Citizen',
    requiresSponsorship: 'No',
    willingToRelocate: 'No',
  },
  documents: [
    { kind: 'resume', fileName: 'jordan-avery-resume.pdf', downloadUrl: '/api/render/resume-base?format=pdf' },
  ],
};
