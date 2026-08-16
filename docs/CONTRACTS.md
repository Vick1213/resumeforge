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

### Tailored headline

A tailoring run's `Basics.Headline` is a deterministic override — never a model decision.
When the job's title (`JobPosting.Title`, §4) was determined at ingest, the tailored
document's headline becomes that title, trimmed and with internal whitespace runs
collapsed to single spaces; it is otherwise used verbatim, never rewritten. When `Title`
is null or blank, `Basics.Headline` keeps the profile headline `build-base` copied from
the knowledge base.

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
[JsonDerivedType(typeof(InjectKeywordsCommand), "injectKeywords")]
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

// Order's JSON Schema constrains each element to an `enum` of the exact wire values
// SectionKind serializes to (JsonSchemaRegistry derives this list from the enum itself,
// never hand-typed, so it cannot drift as members are added).
public sealed record SetSectionOrderCommand : TailorCommand
{ public required IReadOnlyList<SectionKind> Order { get; init; } }

// Weave job-description keywords into an existing bullet for keyword-matching systems.
// Only available at Thorough effort and above, and only for keywords the knowledge base
// already evidences — see validation rule 6. This is the honest form of ATS keyword
// optimization: it surfaces terms the person can actually support, and refuses the rest.
public sealed record InjectKeywordsCommand : TailorCommand
{
    public required string Target { get; init; }                    // "exp:acme-corp#2"
    public required IReadOnlyList<string> Keywords { get; init; }   // normalized names
    public required string Text { get; init; }                      // rewritten bullet
}
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

### Forced inclusion — user pins and excludes override the model

`TailorRequest.PinnedEntryIds` and `TailorRequest.ExcludedEntryIds` (§9) let the user force
specific entries into or out of the tailored resume, overriding both the model's
include/exclude commands and `PageBudgetEnforcer`'s trimming. Both target entry ids only
(`exp:`, `prj:`, `edu:`, `cert:` — never a bullet or skill id), and both are generic across
every entry kind even though the UI initially only sends `prj:` ids.

- **Pins** (`PinnedEntryIds`): after command execution, the entry's `Included` is forced to
  `true` regardless of what the model commanded, and it is added to the page-budget floor —
  see "Page budget" below.
- **Forced excludes** (`ExcludedEntryIds`): after command execution, the entry's `Included`
  is forced to `false` regardless of what the model commanded. A force-excluded entry's
  candidates are also dropped from the brief before the model ever sees them, so it never
  spends a decision on an entry whose outcome is already fixed.
- **Validation**: an id present in both lists is rejected — the request fails with 400,
  never resolved by one list silently winning. An id that does not resolve to a real
  knowledge-base entry fails the same way, naming every offending id in the response.
  Validation runs pre-flight in the endpoint, the same way an unknown `JobId`/`BaseResumeId`
  is checked before the tailoring graph ever runs.
- Null or empty means no forcing, for either list — every pre-existing request shape
  continues to behave exactly as before.

### Rendering

Beyond `Included == false`, all three renderers (`PdfResumeRenderer`, `HtmlResumeRenderer`,
`MarkdownResumeRenderer`) apply one more presentation-only rule: **a project entry with an
empty `Bullets` list and a null-or-blank `Tagline` is omitted from rendered output.** Such
an entry has nothing to show beyond a name and date range, which renders as a bare,
broken-looking line. The entry is untouched in the document model — the diff, coverage
report, and UI still see it — this only decides what a render produces. If every included
project is contentless this way, the PROJECTS heading itself is omitted too, exactly as
when there are no included project entries at all.

The PDF renderer additionally makes the header contact line and project links clickable,
matching what the HTML renderer already does with `<a>` tags. In the header, email links to
`mailto:{email}`; website, LinkedIn, and GitHub link to their full stored URL. In a project
entry's title row, `Url` and `RepoUrl` (when present) render as hyperlinks appended after
the name, in that order. Every hyperlink's *display* text is scheme-stripped and
`www.`-stripped the same way the plain contact line always has been (`https://example.com/`
shows as `example.com`); the link *target* is always the full, untouched URL. Only
clickability changes — font size and color are identical to the surrounding plain text.

`ResumeBuilder`'s default `SectionOrder` is `Summary, Education, Skills, Experience,
Projects, Certifications` — education sits right after the summary rather than at the end,
so a candidate's degree isn't buried below every job and side project. This is only the
default: `SetSectionOrderCommand` still reorders freely, and every renderer follows
whatever `SectionOrder` the document carries, not this list.

Skill groups follow one more rule at build time, in `ResumeBuilder.BuildSkillGroups`: a
taxonomy category left with **exactly one** skill after normalization is folded into the
trailing "Other" group instead of rendering as its own single-item row (a full labeled line
for one skill — "Practices: CI/CD" — reads as broken, not concise). "Other" itself is exempt
from folding, since it is the fold's destination. Categories with two or more skills are
unaffected.

In the PDF and HTML renderers, the header — name, headline, and contact line — is
horizontally centered rather than left-aligned; Markdown has no alignment concept and is
unaffected. The PDF renderer also disables the OpenType "liga" (standard ligatures) font
feature document-wide: a subsetted PDF font's ligature glyph (e.g. the merged "ft" in
"Software") commonly has no `ToUnicode` entry mapping it back to its source letters, so an
ATS parser extracting text from an unmodified PDF sees "So�ware" instead. Disabling
ligatures trades a minor typographic nicety for text that copy-pastes and parses correctly,
which matters more on a document whose main job is to survive automated screening.

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
4. Total `RewriteCommand` count ≤ `TailorOptions.MaxRewrites`, which is derived from
   effort — see below.
5. `OrderCommand.Order` contains no duplicates.
6. `InjectKeywordsCommand` passes rule 3's fabrication guard **and** every keyword it
   names is already evidenced somewhere in the knowledge base — in a skill group, or in
   the text of some entry or bullet. A keyword the person cannot support is rejected
   with code `unsupported-keyword`, never injected. This rule is what separates keyword
   optimization from lying on a resume, and it is not negotiable at any effort level.
   The command is additionally rejected with `op-unavailable-at-effort` when the run's
   effort is below `Thorough`.

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
    public required string Code { get; init; }   // "unknown-target", "fabricated-metric", "malformed-command", ...
}
```

### Model effort — buying more decisions on purpose

Bounded cost is the default, not a ceiling. `ModelEffort` lets the user spend more
deliberately, and it scales the number of *decisions* the model makes — never the amount
of already-written text it restates. The cost story survives because the mechanism is
unchanged: more effort means more commands, not a longer document echoed back.

```csharp
public enum ModelEffort { Minimal, Standard, Thorough, Maximum }
```

| Effort | `MaxRewrites` | Ops additionally enabled | Typical output tokens |
| --- | --- | --- | --- |
| `Minimal` | 0 | — (selection and ordering only) | ~200 |
| `Standard` | 6 | `rewrite`, `setSummary` | ~600 |
| `Thorough` | 12 | + `injectKeywords` | ~1,200 |
| `Maximum` | 20 | + `setSummary` regenerated per run | ~2,000 |

`Standard` is the default and is what every existing behaviour maps to, so effort is
purely additive — an omitted `Effort` must produce byte-identical output to before this
was introduced.

Two rules that hold at **every** level, including `Maximum`:

- The fabrication guard is never relaxed. Higher effort buys more rewriting, never
  permission to invent a metric, an employer, or a date.
- `selectVariant` is still preferred over `rewrite` wherever a suitable KB variant
  exists. Effort raises the cap on rewrites; it does not make rewriting the goal.

`MaxRewrites` remains individually overridable on the request. When both are supplied the
explicit value wins, so effort is a preset rather than a lock.

### Page budget

A resume that does not fit on a page nobody will read is not tailored. Relevance ranking
alone does not bound length: a large knowledge base produces a large resume, because
every entry that scores above zero is still a candidate for inclusion.

`TailorOptions.MaxPages` (default **2**, `null` disables) bounds the rendered result.
Enforcement is deterministic and spends **no model tokens** — it runs after commands are
executed and before the final render:

1. Render the document and count pages.
2. While the count exceeds the budget, exclude the single lowest-scoring still-included
   entry and render again.
3. Stop when it fits, when only the floor remains, or after a bounded number of passes.

Two rules constrain what may be cut:

- **Cut order follows relevance, but section kind breaks ties**: certifications and
  projects are surrendered before experience. A job seeker's employment history is the
  substance of a resume; side projects are the padding.
- **The floor is never crossed**: basics, and the single highest-scoring experience
  entry, are never excluded to satisfy a budget. If a document cannot fit even at the
  floor, it is rendered over budget rather than mutilated, and the result says so.
  `TailorRequest.PinnedEntryIds` (see "Forced inclusion" above) extends the floor: every
  pinned entry, of any cuttable kind, joins the highest-scoring experience entry as never a
  cut candidate. If the floor plus every pin still doesn't fit, the same rule applies —
  rendered over budget, `FitsBudget = false` — rather than cutting a pinned entry to make
  it fit.

Every entry dropped this way is reported as a `ResumeDiffEntry` with `Kind = Excluded`
and a `Rationale` naming the budget, so a cut is never silent — the user can see exactly
what length cost them and raise `MaxPages` if they disagree.

`TailoringResult` carries the final `PageCount`, and `FitsBudget` is false when the floor
was reached before the budget was met.

Estimated token cost per level must be shown in the UI beside the control. A user
choosing to spend more is entitled to know roughly what they are buying before they
click, and the figures above are the contract for that display.

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

    // Populated only when Kind == KeywordsInjected: the keywords that were woven in.
    public IReadOnlyList<string> Keywords { get; init; } = [];
}

public enum DiffKind
{
    Included, Excluded, Reordered, Rewritten,
    VariantSelected, SummarySet, SkillEmphasized,
    KeywordsInjected,
}

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

### Swappable cores

The port has exactly **three** implementations, chosen at composition time. Two of them
are real network clients split by *wire format*, not by vendor — that split is the whole
design, because most hosted and local providers speak OpenAI's chat-completions format,
so one client serves them all.

```
ResumeForge:Ai:Provider = auto | deepseek | anthropic | lmstudio | openai | heuristic
```

Each named value is a **preset** supplying defaults for `BaseUrl`, `Model`, and which
environment variable holds the key. Every preset field stays individually overridable
under `ResumeForge:Ai:*`, so an unlisted provider (Together, Groq, Ollama, vLLM,
OpenRouter) needs config only — never a code change. Do not hardcode a host.

| Preset | Wire | BaseUrl default | Model default | Key |
| --- | --- | --- | --- | --- |
| `deepseek` | OpenAI | `https://api.deepseek.com` | `deepseek-chat` | `DEEPSEEK_API_KEY` |
| `openai` | OpenAI | `https://api.openai.com/v1` | `gpt-4o` | `OPENAI_API_KEY` |
| `lmstudio` | OpenAI | `http://localhost:1234/v1` | *(server's loaded model)* | none |
| `anthropic` | Anthropic | `https://api.anthropic.com` | `claude-sonnet-5` | `ANTHROPIC_API_KEY` |
| `heuristic` | none | — | — | none |

`auto` is the default and resolves in this order: `DEEPSEEK_API_KEY` set → `deepseek`;
else `ANTHROPIC_API_KEY` set → `anthropic`; else `heuristic`. **`auto` never selects
`lmstudio`** — that requires a server the user has deliberately started, and probing the
network during DI registration is not acceptable. Selecting `lmstudio` is explicit.

Implementations in `ResumeForge.Infrastructure.Ai`:

- `OpenAiCompatibleLanguageModel` — serves the `deepseek`, `openai`, and `lmstudio`
  presets and any other OpenAI-format endpoint. Auth is `Authorization: Bearer <key>`,
  omitted entirely when no key is configured (LM Studio accepts none); the endpoint is
  `POST {BaseUrl}/chat/completions`.

  Structured output degrades through three strategies, in order, controlled by
  `ResumeForge:Ai:StructuredOutput` (`auto` by default):

  1. **Forced tool call** — declare one function named `emit_result` whose `parameters`
     is the JSON Schema named by `ModelRequest.SchemaName`, and pin `tool_choice` to it
     so the reply is nothing but the validated argument object.
  2. **JSON schema response format** — `response_format: { "type": "json_schema", ... }`,
     carrying the same schema. This is the path for local servers such as LM Studio,
     whose structured-output support is reliable while forced `tool_choice` is not.
  3. **Prompted JSON** — `response_format: { "type": "json_object" }` with the schema
     inlined into the system prompt, then parse the message content.

  A provider that rejects a strategy (HTTP 400, or a 200 carrying no tool call — which
  `deepseek-reasoner` does, since it does not support forced function calls) falls to the
  next strategy and **remembers the working strategy for the process lifetime**, so the
  cost is one probe, not one per request. Retry once on a schema mismatch, feeding the
  validation error back.

  Token usage maps from `usage.prompt_tokens` and `usage.completion_tokens`. When the
  provider reports a server-side context-cache hit — DeepSeek's
  `usage.prompt_cache_hit_tokens`, OpenAI's `usage.prompt_tokens_details.cached_tokens` —
  map it onto `TokenUsage.CacheHits` so the UI's cost readout reflects real savings
  rather than only the local `CachingLanguageModel` hits.

- `AnthropicLanguageModel` — serves the `anthropic` preset. Anthropic Messages API
  (`POST /v1/messages`), `x-api-key` + `anthropic-version` headers, structured output
  forced via `tool_choice` on a single `emit_result` tool. Usage maps from
  `usage.input_tokens` / `usage.output_tokens`, with `cache_read_input_tokens` onto
  `TokenUsage.CacheHits`.

- `HeuristicLanguageModel` — **no network**, used by `auto` when no key is present, and
  in all tests. Produces valid commands by pure ranking rules (include top-scored
  entries, select best-matching variants, order by relevance, no rewrites). The whole
  product must work end to end with this implementation; the repo must be runnable by a
  stranger with no API key and no local server.

- `CachingLanguageModel` — decorator wrapping whichever core is selected. SHA-256 of
  `(ModelId, System, User, SchemaName)`, backed by the database. `ModelId` must carry the
  resolved provider **and** model (e.g. `lmstudio/qwen3-8b`), so switching cores can
  never serve a cached response generated by a different model.

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
| `GET` | `/api/autofill/fieldmap/{host}?formSignature=` | → `LearnedFieldMap?` |
| `GET` | `/api/applications` | → `ApplicationDto[]` |
| `POST` | `/api/applications` | `CreateApplicationRequest` → `ApplicationDto` |
| `PATCH`| `/api/applications/{id}` | `UpdateApplicationRequest` → `ApplicationDto` |

\* `docx` may return 501 in v1; `pdf`, `html`, `md` are required.

For the pasted-`rawText` branch of `POST /api/jobs` (no fetcher runs, so `Title`/`Company`
are otherwise never set), `JobPosting.Title` is filled in by
`PastedJobTitleExtractor.Extract` (`ResumeForge.Application.Analysis`) — a deterministic
scan of the first 10 non-blank lines for an explicit "Title:"-style label or a short,
role-keyword-bearing line. It is conservative on purpose: the title becomes the tailored
resume's headline, so a miss (`null`) is preferred over a wrong guess.

```csharp
public sealed record TailorRequest
{
    public required string JobId { get; init; }
    public string? BaseResumeId { get; init; }     // null → current base resume
    public ModelEffort Effort { get; init; } = ModelEffort.Standard;
    public int? MaxRewrites { get; init; }         // null → derived from Effort; set wins
    public int? MaxPages { get; init; } = 2;       // null → unbounded; see Page budget
    public bool DryRun { get; init; }              // validate + trace, don't persist
    public IReadOnlyList<string>? PinnedEntryIds { get; init; }    // forced in; see Forced inclusion
    public IReadOnlyList<string>? ExcludedEntryIds { get; init; }  // forced out; see Forced inclusion
}
```

`Effort` serializes as the lowercase name (`minimal`, `standard`, `thorough`, `maximum`).
Omitting it must reproduce the pre-effort behaviour exactly.

CORS: allow `http://localhost:5173` (Vite dev) and `chrome-extension://*` for the
autofill endpoints.

### Response semantics

A response type written with a `?` (only `LearnedFieldMap?`) means **200 with a JSON
`null` body** on a miss — it is a cache probe, and absence is a normal answer. Every
other lookup returns **404** when the entity does not exist. Note that both
`TypedResults.Ok<T>(null)` and `TypedResults.Json<T>(null)` emit an *empty* body rather
than the four bytes `null`, so that one endpoint must write the literal itself.

`POST /api/tailor` persists the tailored document as a **new** resume: the executor
preserves the source document's `Id` and `Name`, so saving the result unmodified would
overwrite the base resume in place. The endpoint assigns a fresh id and a name derived
from the job's company and title, returns that document, and carries the run id in a
`Location: /api/tailor/{runId}/trace` header — `TailoringResult` itself has no run id
field.

`ApplicationStatus` is the closed set below, serialized in exactly this lowercase form.
The frontend's Applications board is keyed on these values; a rename silently empties a
column rather than erroring.

```
saved | applied | screening | interview | offer | rejected | withdrawn
```

`screening` and `interview` are deliberately distinct — a phone screen and an onsite
loop are different states to a job seeker, and collapsing them loses information the
board exists to show. `withdrawn` is distinct from `rejected`: who ended it matters.

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
    public required string DownloadUrl { get; init; }   // ABSOLUTE — see below
}
```

`DownloadUrl` is **absolute**, carrying scheme and authority
(`http://localhost:5217/api/render/resume-base?format=pdf`), never a site-relative path.
The consumer is the extension's background service worker, whose origin is
`chrome-extension://<id>`: a relative URL resolves against the *extension* origin and
fails silently, taking file-upload autofill down with no visible error. The API builds
this from the incoming request's scheme and host rather than a hardcoded value, so it
stays correct behind a proxy or on a different port. Extension-side, treat a relative
value defensively by resolving it against the configured `backendBaseUrl` — the contract
says absolute, but a silent no-op is too expensive a failure to leave undefended.

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
2. **Heuristic matcher** — `Label`, `Name`, `Placeholder` and `AutoComplete` scored by
   normalized token overlap against the canonical key synonym table. Zero model tokens.
   Accepts a match at confidence ≥ 0.72. (These four are exactly what `UnresolvedField`
   carries; there is deliberately no DOM `id` or `aria-label` on the wire.)
3. **Model fallback** — only fields still unresolved, batched into **one** request via
   `POST /api/autofill/resolve`. The response is persisted as a `LearnedFieldMap` keyed by
   `(host, formSignature)`, so the *same form on the next visit costs zero tokens*.

`ModelEffort` tunes where the boundary between tiers 2 and 3 sits. Raising effort raises
the heuristic's accept threshold, so the matcher keeps only what it is confident about
and hands more of the form to the model.

Ownership is split, and the split matters: **the tier-2 threshold is enforced entirely
in the extension**, because tier 2 runs in the browser and the backend never sees a
field the matcher resolved. `ResolveFieldsRequest.Effort` therefore governs only what
tier 3 does with the fields it is given. Both sides read the same table:

| Effort | Tier-2 accept threshold | Tier 3 additionally handles |
| --- | --- | --- |
| `Minimal` | 0.60 | — |
| `Standard` | 0.72 | select/radio option choice |
| `Thorough` | 0.80 | + free-text answers for open questions |
| `Maximum` | 0.88 | + free-text answers, longer budget per answer |

Free-text answers (`Thorough` and above) fill the "describe a project" boxes that
otherwise get left blank. They are subject to the same fabrication rule as resume
bullets: an answer may only assert what the knowledge base supports.

They are grounded in the **knowledge base only**. `ResolveFieldsRequest` deliberately
carries no reference to a job posting — the extension resolves fields on whatever page
the user is on, which is not necessarily a posting ResumeForge has ever seen. That bounds
what this can answer honestly: "describe a project you are proud of" works, "why do you
want to work *here*" does not, and the model must decline the latter rather than invent
enthusiasm about a company it knows nothing about. Grounding answers in a posting would
require threading a job reference through the autofill request, which is a contract
change, not an implementation detail.

`FieldResolution.OptionValue` carries the value to fill in both cases: the chosen option
for a select or radio, and the drafted text for a free-text field. The name is narrower
than its use — kept deliberately rather than renamed, because the extension already
distinguishes the two by the field's `InputType`, and a rename would churn three
components for no behavioural gain.

A learned field map records the effort it was produced at. A map learned at a lower
effort is still a valid cache hit — the point of the cascade is that resolution, once
learned, is free — but re-running a form at higher effort must be able to resolve fields
the earlier pass left unmapped rather than treating the cached map as complete.

```csharp
public sealed record ResolveFieldsRequest
{
    public required string Host { get; init; }             // "boards.greenhouse.io"
    public required string FormSignature { get; init; }    // stable hash of the field set
    public required IReadOnlyList<UnresolvedField> Fields { get; init; }
    public ModelEffort Effort { get; init; } = ModelEffort.Standard;
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
    public ModelEffort LearnedAtEffort { get; init; } = ModelEffort.Standard;
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
- `/tailor` — paste JD URL/text → live graph trace → diff view → export;
  a page-limit control (1 or 2 pages) maps directly to `TailorRequest.MaxPages`;
  a per-project Auto/Always/Never control maps to `TailorRequest.PinnedEntryIds`/`ExcludedEntryIds`
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
