# ResumeForge — Shared Contracts

This file is the **single source of truth** for every type that crosses a component
boundary. Backend, frontend, and the browser extension are built independently against
this document. If a type is described here, implement it exactly: same names, same
casing, same nullability.

**Wire format rule:** all JSON crossing HTTP is `camelCase`. The API configures
`JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase` and
`DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`. C# types are declared in
`PascalCase` and serialized to `camelCase` automatically. Enums serialize as
**camelCase strings** (`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`).

**Target framework:** `net10.0`. C# 13, `<Nullable>enable</Nullable>`,
`<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

---

## 1. Identity: stable addressable IDs

Everything the tailoring engine can act on has a **stable string ID**. The AI never
receives or emits resume prose it does not have to; it emits IDs plus tiny commands.
This is the core token-saving mechanism of the product.

ID grammar (`ResumeForge.Domain.Ids.EntityId`):

```
kind ":" slug [ "#" ordinal ]

exp:acme-corp              an experience entry
exp:acme-corp#2            the 3rd bullet of that entry (0-based ordinal)
prj:graph-runner           a project
prj:graph-runner#0         the 1st bullet of that project
edu:uw-madison             an education entry
skl:languages              a skill group
skl:languages#csharp       a single skill inside a group
cert:az-204                a certification
sum                        the summary block (no slug)
```

Rules:
- `kind` is one of `exp | prj | edu | skl | cert | sum`.
- `slug` is lowercase `[a-z0-9-]+`, derived from the source markdown filename (without
  extension), so IDs are stable across edits and reorders.
- Ordinals address bullets/items **within** a parent and are 0-based.
- IDs are case-sensitive and must round-trip: `EntityId.Parse(id.ToString()) == id`.

---

## 2. Canonical resume model

Namespace `ResumeForge.Domain.Resume`. All of these are `record` types with
`init`-only properties. Collections are `IReadOnlyList<T>` and never null (empty instead).

```csharp
public sealed record ResumeDocument
{
    public required string Id { get; init; }              // guid string
    public required string Name { get; init; }            // "Base resume", "Acme - Backend Eng"
    public required ResumeBasics Basics { get; init; }
    public string? Summary { get; init; }
    public required IReadOnlyList<SkillGroup> Skills { get; init; }
    public required IReadOnlyList<ExperienceEntry> Experience { get; init; }
    public required IReadOnlyList<ProjectEntry> Projects { get; init; }
    public required IReadOnlyList<EducationEntry> Education { get; init; }
    public required IReadOnlyList<CertificationEntry> Certifications { get; init; }
    public required IReadOnlyList<SectionKind> SectionOrder { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public enum SectionKind { Summary, Skills, Experience, Projects, Education, Certifications }

public sealed record ResumeBasics
{
    public required string FullName { get; init; }
    public string? Headline { get; init; }        // "Senior Backend Engineer"
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Location { get; init; }        // "Seattle, WA"
    public string? Website { get; init; }
    public string? LinkedIn { get; init; }        // full URL
    public string? GitHub { get; init; }          // full URL
}

public sealed record Bullet
{
    public required string Id { get; init; }      // "exp:acme-corp#0"
    public required string Text { get; init; }    // the variant currently selected
    public IReadOnlyList<string> Variants { get; init; } = [];  // alternates from the KB
    public IReadOnlyList<string> Tags { get; init; } = [];      // normalized skill tags
    public double Relevance { get; init; }        // 0..1, set by scoring, 0 when unscored
}

public sealed record ExperienceEntry
{
    public required string Id { get; init; }      // "exp:acme-corp"
    public required string Role { get; init; }
    public required string Organization { get; init; }
    public string? Location { get; init; }
    public required DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }       // null == present
    public required IReadOnlyList<Bullet> Bullets { get; init; }
    public IReadOnlyList<string> Tech { get; init; } = [];
    public bool Included { get; init; } = true;   // tailoring may exclude without deleting
}

public sealed record ProjectEntry
{
    public required string Id { get; init; }      // "prj:graph-runner"
    public required string Name { get; init; }
    public string? Tagline { get; init; }
    public string? Url { get; init; }
    public string? RepoUrl { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public required IReadOnlyList<Bullet> Bullets { get; init; }
    public IReadOnlyList<string> Tech { get; init; } = [];
    public bool Included { get; init; } = true;
}

public sealed record EducationEntry
{
    public required string Id { get; init; }
    public required string Institution { get; init; }
    public required string Credential { get; init; }   // "B.S. Computer Science"
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? Location { get; init; }
    public double? Gpa { get; init; }
    public IReadOnlyList<string> Highlights { get; init; } = [];
    public bool Included { get; init; } = true;
}

public sealed record CertificationEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Issuer { get; init; }
    public DateOnly? IssuedOn { get; init; }
    public string? CredentialUrl { get; init; }
    public bool Included { get; init; } = true;
}

public sealed record SkillGroup
{
    public required string Id { get; init; }      // "skl:languages"
    public required string Label { get; init; }   // "Languages"
    public required IReadOnlyList<Skill> Items { get; init; }
    public bool Included { get; init; } = true;
}

public sealed record Skill
{
    public required string Id { get; init; }      // "skl:languages#csharp"
    public required string Name { get; init; }    // display form: "C#"
    public required string Normalized { get; init; } // match form: "csharp"
    public bool Emphasized { get; init; }
}
```

---

## 3. Knowledge base markdown format

Namespace `ResumeForge.Domain.Knowledge`. Files live under `profile/` at the repo root
(configurable via `ResumeForge:ProfileRoot`). One entity per file. The **filename is the
slug**: `profile/experience/acme-corp.md` → `exp:acme-corp`.

Frontmatter is YAML between `---` fences. Body is markdown; `-` list items at the top
level of the body are bullets, and an indented `-` beneath a bullet is a **variant** of
that bullet (a shorter or differently-angled phrasing of the same accomplishment).

`profile/experience/acme-corp.md`:

```markdown
---
type: experience
role: Senior Software Engineer
organization: Acme Corp
location: Seattle, WA
startDate: 2022-03
endDate: 2024-11        # omit or use `present` for current roles
tech: [C#, .NET, PostgreSQL, Kubernetes]
tags: [backend, distributed-systems, performance]
---

- Cut p99 checkout latency from 840ms to 120ms by replacing a serial fan-out with a
  bounded parallel scatter-gather over 6 downstream services.
  - Rebuilt checkout fan-out as a bounded scatter-gather, cutting p99 from 840ms to 120ms.
- Led migration of 40+ services from .NET 6 to .NET 8, retiring 12k lines of shim code.
```

`profile/projects/graph-runner.md`:

```markdown
---
type: project
name: Graph Runner
tagline: A DAG executor for multi-agent pipelines
url: https://example.com/graph-runner
repoUrl: https://github.com/user/graph-runner
startDate: 2024-01
endDate: present
tech: [TypeScript, Node.js]
tags: [orchestration, concurrency]
source: github        # `manual` | `github`; github-imported files are safe to regenerate
stars: 412            # optional, github-imported
---

- Built a scheduler that streams work items through stages without a barrier, cutting
  wall-clock time 3x versus a staged design.
```

`profile/education/uw-seattle.md` — body list items become `EducationEntry.Highlights`
(education entries have highlights, not bullets, so variants are not supported here):

```markdown
---
type: education
institution: University of Washington
credential: B.S. Computer Science
location: Seattle, WA
startDate: 2014-09
endDate: 2018-06
gpa: 3.8
---

- Graduated with departmental honors; capstone built a Raft-backed key-value store.
```

`profile/certifications/cka.md` — frontmatter only; the body is ignored:

```markdown
---
type: certification
name: Certified Kubernetes Administrator (CKA)
issuer: Cloud Native Computing Foundation
issuedOn: 2022-11
credentialUrl: https://www.credly.com/badges/example
---
```

`profile/basics.md` — a single file whose frontmatter maps key-for-key onto
`ResumeBasics` (`fullName`, `headline`, `email`, `phone`, `location`, `website`,
`linkedin`, `github`) and whose body is the default summary text.

### Skills are derived, not authored

There is deliberately **no `profile/skills/` directory and no skills markdown file.**
Skill groups are synthesized by `ResumeBuilder` from the `tech:` frontmatter of every
experience and project entry: each value is normalized through `SkillNormalizer` and
`ISkillTaxonomy`, then bucketed into a `SkillGroup` per taxonomy category (`languages`,
`frameworks`, `datastores`, `cloud`, `practices`, `tools`, `soft`), with groups emitted
in that fixed order and skills sorted alphabetically by display name within a group.

The rationale is that a hand-maintained skills list drifts out of sync with the work it
is supposed to summarize. Deriving it means a skill can only appear on the resume if
some entry actually evidences it, which is also what makes `CoverageAnalyzer`'s evidence
links meaningful. A skill group's `Included` flag and a skill's `Emphasized` flag are
then set per-run by the tailoring commands.

**Parser contract** (`IKnowledgeBaseReader`):
- Unknown frontmatter keys are preserved in `IReadOnlyDictionary<string, string> Extra`,
  never an error.
- `startDate` / `endDate` accept `yyyy-MM`, `yyyy-MM-dd`, `yyyy`, or `present`.
- A malformed file yields a `KnowledgeBaseDiagnostic` (file, line, message, severity)
  and is skipped; one bad file never fails the whole load.
- Writing is round-trip stable: read → write produces a byte-identical file when nothing
  changed. Frontmatter keys are emitted in the documented order above.

---

## 4. Job description analysis (deterministic — no model call)

Namespace `ResumeForge.Application.Analysis`.

```csharp
public sealed record JobPosting
{
    public required string Id { get; init; }
    public required string SourceUrl { get; init; }
    public string? Company { get; init; }
    public string? Title { get; init; }
    public string? Location { get; init; }
    public required string RawText { get; init; }
    public DateTimeOffset FetchedAt { get; init; }
}

public sealed record JobAnalysis
{
    public required string JobId { get; init; }
    public required IReadOnlyList<Requirement> Requirements { get; init; }
    public required IReadOnlyList<string> Keywords { get; init; }       // normalized, ranked
    public required IReadOnlyList<string> MatchedSkills { get; init; }  // recognized by the taxonomy
    public required IReadOnlyList<string> MissingSkills { get; init; }  // skill-like, taxonomy unknown
    public required SeniorityLevel Seniority { get; init; }
}

public enum SeniorityLevel { Unknown, Intern, Junior, Mid, Senior, Staff, Principal }

public sealed record Requirement
{
    public required string Id { get; init; }        // "req:0"
    public required string Text { get; init; }
    public required RequirementKind Kind { get; init; }
    public required bool IsMandatory { get; init; }  // "required" vs "nice to have"
    public IReadOnlyList<string> Skills { get; init; } = [];
    public double Weight { get; init; }             // 0..1
}

public enum RequirementKind { Skill, Experience, Education, Responsibility, Other }
```

`MatchedSkills` and `MissingSkills` are deliberately scoped to the **taxonomy**, not to
the knowledge base. `JobAnalyzer` depends only on `ISkillTaxonomy`, so it can be run and
cached per job posting independently of whose resume it is later matched against, and it
stays deterministic. `MatchedSkills` therefore means "this JD term is a skill the
taxonomy recognizes" and `MissingSkills` means "this looks like a skill but the taxonomy
has no entry for it" — the latter is the signal for growing `skills.json`. Reconciling a
posting against a specific candidate's evidence is `CoverageAnalyzer`'s job, further down
the graph, and that is what `RequirementCoverage.EvidenceIds` reports.

The analyzer is pure C#: sentence segmentation, a bundled skill taxonomy
(`ResumeForge.Infrastructure/Data/skills.json` — alias → canonical), section detection
by heading regex, and mandatory/optional classification by cue phrases. **No LLM call
happens in this stage**, and its output must be deterministic for a given input.

---

## 5. Relevance scoring (deterministic — no model call)

Namespace `ResumeForge.Application.Scoring`. BM25 over bullet text plus a skill-overlap
term and a recency term. Produces the candidate set that gets sent to the model.

```csharp
public sealed record ScoredCandidate
{
    public required string EntityId { get; init; }   // bullet or entry ID
    public required string Text { get; init; }
    public required double Score { get; init; }      // 0..1
    public required IReadOnlyList<string> MatchedRequirements { get; init; }  // requirement IDs
}

public sealed record CandidateSet
{
    public required IReadOnlyList<ScoredCandidate> Experience { get; init; }
    public required IReadOnlyList<ScoredCandidate> Projects { get; init; }
    public required IReadOnlyList<ScoredCandidate> Skills { get; init; }
}
```

---

## 6. Tailoring commands — the model's entire output surface

Namespace `ResumeForge.Application.Tailoring`. **This is the heart of the design.** The
model never writes or formats a resume. It receives a compact brief (requirements +
candidate IDs with truncated text) and returns *only* a list of commands. Commands are
applied by deterministic C# to the canonical `ResumeDocument`, which is then rendered by
templates. A tailoring run's model output is typically under 600 tokens.

Polymorphic JSON with discriminator `"op"`:

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(IncludeCommand),        "include")]
[JsonDerivedType(typeof(ExcludeCommand),        "exclude")]
[JsonDerivedType(typeof(OrderCommand),          "order")]
[JsonDerivedType(typeof(SelectVariantCommand),  "selectVariant")]
[JsonDerivedType(typeof(RewriteCommand),        "rewrite")]
[JsonDerivedType(typeof(SetSummaryCommand),     "setSummary")]
[JsonDerivedType(typeof(EmphasizeSkillsCommand),"emphasizeSkills")]
[JsonDerivedType(typeof(SetSectionOrderCommand),"setSectionOrder")]
public abstract record TailorCommand
{
    public string? Rationale { get; init; }   // one short clause, shown in the diff UI
}

public sealed record IncludeCommand : TailorCommand
{ public required IReadOnlyList<string> Targets { get; init; } }

public sealed record ExcludeCommand : TailorCommand
{ public required IReadOnlyList<string> Targets { get; init; } }


// Reorder children of a parent. `Order` lists child IDs; any child omitted keeps its
// relative position after the listed ones.
public sealed record OrderCommand : TailorCommand
{
    public required string Parent { get; init; }          // "exp:acme-corp" or "root"
    public required IReadOnlyList<string> Order { get; init; }
}

// Pick an existing phrasing from the KB. Costs zero generation tokens — always
// preferred over `rewrite` when a suitable variant exists.
public sealed record SelectVariantCommand : TailorCommand
{
    public required string Target { get; init; }          // "exp:acme-corp#0"
    public required int VariantIndex { get; init; }
}

// The only command that emits prose. Budget-capped: see MaxRewrites below.
public sealed record RewriteCommand : TailorCommand
{
    public required string Target { get; init; }
    public required string Text { get; init; }
}

public sealed record SetSummaryCommand : TailorCommand
{ public required string Text { get; init; } }

public sealed record EmphasizeSkillsCommand : TailorCommand
{ public required IReadOnlyList<string> Skills { get; init; } }  // normalized names

public sealed record SetSectionOrderCommand : TailorCommand
{ public required IReadOnlyList<SectionKind> Order { get; init; } }
```

### Include/exclude semantics

`include` and `exclude` can target two different kinds of node, and they behave
differently on each. This is deliberate, and both executor and renderers depend on it:

- **Entries** (`exp:`, `prj:`, `edu:`, `cert:`) and **skill groups** (`skl:`) carry an
  `Included` boolean. Excluding one flips the flag to `false`; the entry stays in the
  document so the diff, the coverage report, and the UI can still refer to it, and
  renderers skip anything with `Included == false`.
- **Bullets** (`exp:acme#2`) and **individual skills** (`skl:languages#csharp`) have no
  such flag. Excluding one **removes it from its parent's list** in the produced
  document. The removal is recorded as a `ResumeDiffEntry` with `Kind = Excluded` and
  `Before` set to the original text, so nothing is lost from the audit trail.

Because bullet removal renumbers nothing — IDs are assigned from the knowledge base at
build time and never recomputed — an excluded bullet's ID does not get reused by its
former siblings. `exp:acme#0, exp:acme#1, exp:acme#2` with `#1` excluded renders two
bullets that keep the IDs `#0` and `#2`. Executors must not renumber.

### Command validation (`ICommandValidator`) — runs before execution

A command is **rejected**, not clamped, if it fails any of these. Rejections are
reported, never silently dropped:

1. Every `Target` / `Parent` / `Order` entry parses as an `EntityId` and resolves to an
   existing node in the document.
2. `SelectVariantCommand.VariantIndex` is within the target bullet's `Variants` range.
3. `RewriteCommand.Text` is ≤ 300 characters, is a single line, and shares at least one
   number or proper noun with the original bullet when the original contained one —
   this is the **anti-fabrication check**: the model may re-angle a bullet but may not
   invent metrics. Implemented as `IFabricationGuard`.
4. Total `RewriteCommand` count ≤ `TailorOptions.MaxRewrites` (default 6).
5. `OrderCommand.Order` contains no duplicates.

```csharp
public sealed record CommandValidationResult
{
    public required IReadOnlyList<TailorCommand> Accepted { get; init; }
    public required IReadOnlyList<RejectedCommand> Rejected { get; init; }
}

public sealed record RejectedCommand
{
    public required TailorCommand Command { get; init; }
    public required string Reason { get; init; }
    public required string Code { get; init; }   // "unknown-target", "fabricated-metric", ...
}
```

### Tailoring result

```csharp
public sealed record TailoringResult
{
    public required ResumeDocument Document { get; init; }
    public required IReadOnlyList<ResumeDiffEntry> Diff { get; init; }
    public required CommandValidationResult Commands { get; init; }
    public required CoverageReport Coverage { get; init; }
    public required TokenUsage Usage { get; init; }
    public required IReadOnlyList<GraphNodeTrace> Trace { get; init; }
}

public sealed record ResumeDiffEntry
{
    public required string EntityId { get; init; }
    public required DiffKind Kind { get; init; }
    public string? Before { get; init; }
    public string? After { get; init; }
    public string? Rationale { get; init; }
}

public enum DiffKind { Included, Excluded, Reordered, Rewritten, VariantSelected, SummarySet, SkillEmphasized }

public sealed record CoverageReport
{
    public required double Score { get; init; }   // 0..1 — mandatory requirements evidenced
    public required IReadOnlyList<RequirementCoverage> Requirements { get; init; }
}

public sealed record RequirementCoverage
{
    public required string RequirementId { get; init; }
    public required string RequirementText { get; init; }
    public required bool Covered { get; init; }
    public required IReadOnlyList<string> EvidenceIds { get; init; }
}

public sealed record TokenUsage
{
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required int ModelCalls { get; init; }
    public required int CacheHits { get; init; }
}
```

---

## 7. Graph engine

Namespace `ResumeForge.Application.Graph`. A small, dependency-free DAG executor. The
tailoring pipeline is *declared* as a graph, not written as a straight line, and the
executor decides what runs concurrently.

```csharp
public interface IGraphNode
{
    string Name { get; }
    IReadOnlyList<string> DependsOn { get; }
    Task<object?> ExecuteAsync(GraphContext context, CancellationToken ct);
}

public sealed class GraphContext
{
    public T Get<T>(string nodeName);              // typed read of an upstream result
    public bool TryGet<T>(string nodeName, out T value);
    public void Set(string key, object? value);
    public IServiceProvider Services { get; }
    public ITokenBudget Budget { get; }
    public IReadOnlyList<GraphNodeTrace> Trace { get; }
}

public sealed record GraphNodeTrace
{
    public required string Node { get; init; }
    public required GraphNodeStatus Status { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? Error { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
}

public enum GraphNodeStatus { Succeeded, Failed, Skipped, Cancelled }
```

Executor requirements:
- Topological scheduling with **maximum concurrency**: every node whose dependencies are
  satisfied starts immediately; it does not wait on unrelated branches (no artificial
  barriers). Concurrency capped by `GraphOptions.MaxConcurrency`
  (default `Environment.ProcessorCount - 1`, floor 1).
- **Failure isolation:** a failed node marks its transitive dependents `Skipped` and
  records the error; independent branches complete. The run returns a partial result plus
  the trace rather than throwing, unless the failed node was declared `Critical = true`.
- **Cycle detection** at build time — `GraphBuilder.Build()` throws `GraphCycleException`
  naming the cycle.
- **Conditional edges:** `builder.AddNode(...).When(ctx => predicate)` — a node whose
  predicate is false is `Skipped` and its dependents still run, receiving `default`.
- Every node's duration and token usage lands in `Trace`, which is returned to the client
  and rendered as a live pipeline view in the React app.

The tailoring graph (declared in `TailoringGraphFactory`):

```
        fetch-jd ──► analyze-jd ──┬──► score-experience ─┐
                                  ├──► score-projects ───┼──► build-brief ──► propose-commands
        load-kb ──► build-base ───┴──► score-skills ─────┘         (model)         (model)
              │                                                                        │
              └────────────────────────────────────────────────► validate-commands ◄───┘
                                                                         │
                                                    ┌────────────────────┼───────────────┐
                                                    ▼                    ▼               ▼
                                            verify-fabrication   verify-coverage   execute-commands
                                                    └────────────────────┼───────────────┘
                                                                         ▼
                                                                      render
```

`score-experience`, `score-projects`, `score-skills` run concurrently. The two verifier
nodes run concurrently with each other. `propose-commands` is the only node that spends
generation tokens; `verify-*` nodes are deterministic C#.

---

## 8. Language model port

Namespace `ResumeForge.Application.Abstractions`.

```csharp
public interface ILanguageModel
{
    string ModelId { get; }
    Task<ModelResponse<T>> CompleteAsync<T>(ModelRequest request, CancellationToken ct);
}

public sealed record ModelRequest
{
    public required string System { get; init; }
    public required string User { get; init; }
    public required string SchemaName { get; init; }   // names the JSON schema to enforce
    public int MaxOutputTokens { get; init; } = 1024;
    public double Temperature { get; init; } = 0.2;
    public string? CacheKey { get; init; }             // non-null enables response caching
}

public sealed record ModelResponse<T>
{
    public required T Value { get; init; }
    public required TokenUsage Usage { get; init; }
    public required bool FromCache { get; init; }
}
```

Implementations in `ResumeForge.Infrastructure.Ai`:
- `DeepSeekLanguageModel` — the real client. Base URL from `ResumeForge:Ai:BaseUrl`
  (default `https://api.deepseek.com`), model id from `ResumeForge:Ai:Model` (default
  `deepseek-chat`), API key from `DEEPSEEK_API_KEY` or `ResumeForge:Ai:ApiKey`. Auth is
  `Authorization: Bearer <key>`; the endpoint is `POST /chat/completions`.

  Structured output is forced with OpenAI-style function calling: declare one function
  named `emit_result` whose `parameters` is the JSON Schema named by
  `ModelRequest.SchemaName`, and pin `tool_choice` to it so the reply is nothing but the
  validated argument object. If the response carries no tool call — which
  `deepseek-reasoner` can do, since it does not support forced function calls — fall
  back to re-requesting with `response_format: { "type": "json_object" }` and parsing the
  message content. Retry once on a schema mismatch, feeding the validation error back.

  Token usage maps from `usage.prompt_tokens` and `usage.completion_tokens`. DeepSeek
  also reports `usage.prompt_cache_hit_tokens` from its own server-side context cache;
  map that onto `TokenUsage.CacheHits` so the UI's cost readout reflects real savings
  rather than only the local `CachingLanguageModel` hits.

  Because DeepSeek speaks the OpenAI chat-completions wire format, `BaseUrl` being
  configurable means this one class also targets OpenAI, Together, Groq, or a local
  Ollama endpoint without a code change. That is a property worth keeping, not an
  accident — do not hardcode the host.
- `HeuristicLanguageModel` — **no network**, used when no API key is present and in all
  tests. Produces valid commands by pure ranking rules (include top-scored entries,
  select best-matching variants, order by relevance, no rewrites). The whole product must
  work end to end with this implementation; the repo must be runnable by a stranger with
  no API key. Registration picks it automatically when `DEEPSEEK_API_KEY` is unset.
- `CachingLanguageModel` — decorator, SHA-256 of `(ModelId, System, User, SchemaName)`,
  backed by the database.

---

## 9. HTTP API

Base path `/api`. ASP.NET Core minimal APIs grouped per resource, OpenAPI at `/openapi/v1.json`,
Scalar UI at `/docs`. All errors use RFC 9457 `ProblemDetails`.

| Method | Route | Body → Response |
| --- | --- | --- |
| `GET` | `/api/profile` | → `ProfileDto` (basics + counts + diagnostics) |
| `PUT` | `/api/profile/basics` | `ResumeBasics` → `ResumeBasics` |
| `GET` | `/api/knowledge` | → `KnowledgeItemDto[]` |
| `GET` | `/api/knowledge/{id}` | → `KnowledgeItemDetailDto` (includes raw markdown) |
| `PUT` | `/api/knowledge/{id}` | `UpsertKnowledgeRequest` → `KnowledgeItemDetailDto` |
| `DELETE` | `/api/knowledge/{id}` | → 204 |
| `POST` | `/api/knowledge/import/github` | `GitHubImportRequest` → `GitHubImportResult` |
| `GET` | `/api/resumes` | → `ResumeSummaryDto[]` |
| `GET` | `/api/resumes/{id}` | → `ResumeDocument` |
| `POST` | `/api/resumes/base` | → `ResumeDocument` (rebuild base resume from the KB) |
| `POST` | `/api/jobs` | `CreateJobRequest { url?, rawText? }` → `JobPosting` |
| `GET` | `/api/jobs/{id}/analysis` | → `JobAnalysis` |
| `POST` | `/api/tailor` | `TailorRequest` → `TailoringResult` |
| `GET` | `/api/tailor/{runId}/trace` | → `GraphNodeTrace[]` |
| `POST` | `/api/render/{resumeId}` | `RenderRequest { format }` → file (`pdf`/`html`/`md`/`docx`\*) |
| `GET` | `/api/autofill/profile` | → `AutofillProfile` (used by the extension) |
| `POST` | `/api/autofill/resolve` | `ResolveFieldsRequest` → `ResolveFieldsResponse` |
| `POST` | `/api/autofill/fieldmap` | `LearnedFieldMap` → 204 (persist a learned map) |
| `GET` | `/api/autofill/fieldmap/{host}` | → `LearnedFieldMap?` |
| `GET` | `/api/applications` | → `ApplicationDto[]` |
| `POST` | `/api/applications` | `CreateApplicationRequest` → `ApplicationDto` |
| `PATCH`| `/api/applications/{id}` | `UpdateApplicationRequest` → `ApplicationDto` |

\* `docx` may return 501 in v1; `pdf`, `html`, `md` are required.

```csharp
public sealed record TailorRequest
{
    public required string JobId { get; init; }
    public string? BaseResumeId { get; init; }     // null → current base resume
    public int MaxRewrites { get; init; } = 6;
    public bool DryRun { get; init; }              // validate + trace, don't persist
}
```

CORS: allow `http://localhost:5173` (Vite dev) and `chrome-extension://*` for the
autofill endpoints.

---

## 10. Autofill contracts

Namespace `ResumeForge.Application.Autofill`. Shared verbatim with the extension
(mirrored in `extension/src/contracts.ts`).

```csharp
public sealed record AutofillProfile
{
    public required IReadOnlyDictionary<string, string?> Fields { get; init; }  // canonical key → value
    public required IReadOnlyList<AutofillDocument> Documents { get; init; }
}

public sealed record AutofillDocument
{
    public required string Kind { get; init; }     // "resume" | "coverLetter"
    public required string FileName { get; init; }
    public required string DownloadUrl { get; init; }
}
```

**Canonical field keys** (closed set — the extension and backend must agree exactly):

```
firstName, lastName, fullName, preferredName, email, phone,
addressLine1, addressLine2, city, state, postalCode, country,
linkedin, github, portfolio, website,
currentCompany, currentTitle, yearsExperience,
workAuthorization, requiresSponsorship, willingToRelocate,
noticePeriod, desiredSalary, availableStartDate,
gender, ethnicity, veteranStatus, disabilityStatus,
howDidYouHear, referredBy
```

Resolution is a three-tier cascade, and the tiers matter — this is the extension's
token-saving story:

1. **Board adapter** (`extension/src/adapters/*.ts`) — declarative selector maps for
   Greenhouse, Lever, Ashby, Workday, SmartRecruiters. Zero model tokens.
2. **Heuristic matcher** — label text, `name`/`id`/`autocomplete` attributes, placeholder,
   and `aria-label` scored by normalized token overlap against the canonical key
   synonym table. Zero model tokens. Accepts a match at confidence ≥ 0.72.
3. **Model fallback** — only fields still unresolved, batched into **one** request via
   `POST /api/autofill/resolve`. The response is persisted as a `LearnedFieldMap` keyed by
   `(host, formSignature)`, so the *same form on the next visit costs zero tokens*.

```csharp
public sealed record ResolveFieldsRequest
{
    public required string Host { get; init; }             // "boards.greenhouse.io"
    public required string FormSignature { get; init; }    // stable hash of the field set
    public required IReadOnlyList<UnresolvedField> Fields { get; init; }
}

public sealed record UnresolvedField
{
    public required string ElementId { get; init; }        // extension-assigned, stable per render
    public string? Label { get; init; }
    public string? Name { get; init; }
    public string? Placeholder { get; init; }
    public string? AutoComplete { get; init; }
    public required string InputType { get; init; }        // text|email|tel|select|radio|checkbox|file|textarea
    public IReadOnlyList<string> Options { get; init; } = [];  // for select/radio
}

public sealed record ResolveFieldsResponse
{
    public required IReadOnlyList<FieldResolution> Resolutions { get; init; }
    public required TokenUsage Usage { get; init; }
}

public sealed record FieldResolution
{
    public required string ElementId { get; init; }
    public required string CanonicalKey { get; init; }     // "" when genuinely unmappable
    public required double Confidence { get; init; }
    public string? OptionValue { get; init; }              // chosen option for select/radio
}

public sealed record LearnedFieldMap
{
    public required string Host { get; init; }
    public required string FormSignature { get; init; }
    public required IReadOnlyDictionary<string, string> ElementToKey { get; init; }
    public required DateTimeOffset LearnedAt { get; init; }
    public int HitCount { get; init; }
}
```

The extension **never auto-submits a form** and never fills a field the user has already
typed into. Every fill is previewed with a per-field accept/reject overlay.

---

## 11. Persistence

EF Core 10 with SQLite (`ResumeForge.Infrastructure.Persistence.ResumeForgeDbContext`).
The markdown files under `profile/` remain the source of truth for knowledge items; the
database holds derived and operational state only:

`Resumes`, `JobPostings`, `JobAnalyses`, `TailoringRuns`, `ModelCacheEntries`,
`LearnedFieldMaps`, `Applications`.

Complex sub-objects are stored as JSON columns via `.HasConversion`. Migrations are
committed to the repo. `dotnet run --project backend/src/ResumeForge.Api` applies
migrations automatically in Development.

---

## 12. Frontend

React 19 + TypeScript (strict) + Vite + TanStack Query + Tailwind. No component library
beyond Radix primitives. Routes:

- `/` — dashboard: KB counts, recent tailoring runs, application funnel
- `/knowledge` — list + markdown editor with live frontmatter validation
- `/knowledge/import` — GitHub import (pick repos → preview generated markdown → commit)
- `/tailor` — paste JD URL/text → live graph trace → diff view → export
- `/applications` — tracker board
- `/settings` — profile basics, API key presence (never the value), model selection

`frontend/src/api/types.ts` is **generated** from the OpenAPI document
(`npm run generate:api`), so contract drift fails the build rather than the user.

---

## 13. Conventions

- Commits: Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:`, `test:`).
  Never reference the tooling used to write the code in a commit message.
- C#: file-scoped namespaces, `sealed` by default, primary constructors for DI,
  `TimeProvider` instead of `DateTime.Now` (injected, so tests are deterministic).
- Tests: xUnit v3 + Shouldly + NSubstitute. (Shouldly rather than FluentAssertions —
  FluentAssertions v8 is no longer free for commercial use and this is a public repo.)
  Integration tests use `WebApplicationFactory` with an in-memory SQLite connection.
- Every public type gets an XML doc comment on the type; members only where non-obvious.
- No `Console.WriteLine` in library code — `ILogger<T>` only.
