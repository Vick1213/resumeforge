# Knowledge base format

Every fact ResumeForge knows about you — a role, a project, a degree, a certification —
is one markdown file under `profile/`. This is the format in full, as implemented by
`IKnowledgeBaseReader` per [`docs/CONTRACTS.md` §3](CONTRACTS.md). If you're editing
files by hand, this page plus the worked examples in `profile/` is everything you need.

## Why markdown, not the database

The database (`ResumeForge.Infrastructure.Persistence.ResumeForgeDbContext`) stores
tailoring runs, model cache entries, and applications — none of that is knowledge about
you. The knowledge base lives in files you can read in any editor, diff in `git`, and
own without needing the app running. See `docs/architecture.md` for the fuller argument;
the short version is that a resume is a personal record, not application state, and it
should survive the app being deleted.

## Layout

```
profile/
├── basics.md                  # your ResumeBasics — one file, no directory
├── experience/
│   └── acme-corp.md           # exp:acme-corp
├── projects/
│   └── graph-runner.md        # prj:graph-runner
├── education/
│   └── uw-madison.md          # edu:uw-madison
└── certifications/
    └── az-204.md               # cert:az-204
```

One entity per file. **The filename is the slug** — `profile/experience/acme-corp.md`
becomes ID `exp:acme-corp`. Renaming a file changes its ID; editing its contents never
does. This is why IDs are stable across content edits and bullet reorders, and why you
rename a file rather than looking for an `id:` frontmatter key — there isn't one.

## Frontmatter, by type

Frontmatter is YAML between `---` fences. Unknown keys are never an error — they're
preserved in an `Extra` dictionary so the format can grow without breaking old files.
`startDate` / `endDate` accept `yyyy-MM`, `yyyy-MM-dd`, `yyyy`, or the literal `present`.

### `experience/*.md`

| Key | Required | Notes |
| --- | --- | --- |
| `type` | yes | always `experience` |
| `role` | yes | job title |
| `organization` | yes | employer name |
| `location` | no | free text, e.g. `Seattle, WA` |
| `startDate` | yes | |
| `endDate` | no | omit or `present` for a current role |
| `tech` | no | YAML flow sequence, e.g. `[C#, .NET, PostgreSQL]` |
| `tags` | no | normalized topic tags used by scoring |

```markdown
---
type: experience
role: Senior Software Engineer
organization: Acme Corp
location: Seattle, WA
startDate: 2022-03
endDate: 2024-11
tech: [C#, .NET, PostgreSQL, Kubernetes]
tags: [backend, distributed-systems, performance]
---

- Cut p99 checkout latency from 840ms to 120ms by replacing a serial fan-out with a
  bounded parallel scatter-gather over 6 downstream services.
  - Rebuilt checkout fan-out as a bounded scatter-gather, cutting p99 from 840ms to 120ms.
- Led migration of 40+ services from .NET 6 to .NET 8, retiring 12k lines of shim code.
```

A live example with the variant convention used throughout: `profile/experience/nimbus-systems.md`.

### `projects/*.md`

| Key | Required | Notes |
| --- | --- | --- |
| `type` | yes | always `project` |
| `name` | yes | display name |
| `tagline` | no | one line |
| `url` | no | live site, if any |
| `repoUrl` | no | source repository |
| `startDate` | no | |
| `endDate` | no | omit or `present` |
| `tech` | no | flow sequence |
| `tags` | no | flow sequence |
| `source` | no | `manual` or `github` — see below |
| `stars` | no | GitHub star count; only meaningful when `source: github` |

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
source: github
stars: 412
---

- Built a scheduler that streams work items through stages without a barrier, cutting
  wall-clock time 3x versus a staged design.
```

A hand-written project omits `source` or sets it to `manual` — see
`profile/projects/ledgerline.md`. A GitHub-imported one sets `source: github` and
usually `stars` — see `profile/projects/flowmesh.md`.

### `education/*.md`

| Key | Required | Notes |
| --- | --- | --- |
| `type` | yes | always `education` |
| `institution` | yes | school name |
| `credential` | yes | e.g. `B.S. Computer Science` |
| `location` | no | free text |
| `startDate` | no | |
| `endDate` | no | omit or `present` |
| `gpa` | no | numeric |

Body list items become `EducationEntry.Highlights`, not bullets — **education entries
have highlights, not bullets, so the indented-variant convention does not apply here.**
An indented `-` under a highlight line is not parsed as an alternate phrasing the way it
is for experience and project bullets; write each highlight as a single top-level item.

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

- Graduated with departmental honors; senior capstone built a distributed key-value
  store with Raft-based leader election.
```

See `profile/education/university-of-washington.md`.

### `certifications/*.md`

| Key | Required | Notes |
| --- | --- | --- |
| `type` | yes | always `certification` |
| `name` | yes | e.g. `Certified Kubernetes Administrator (CKA)` |
| `issuer` | no | issuing organization |
| `issuedOn` | no | |
| `credentialUrl` | no | link to the credential/badge |

Certifications carry no bullets or highlights — there's nothing in the canonical model
to attach them to. **The body is ignored** by the parser; frontmatter is the whole
entity.

```markdown
---
type: certification
name: AWS Certified Solutions Architect - Professional
issuer: Amazon Web Services
issuedOn: 2023-05
credentialUrl: https://www.credly.com/badges/example
---
```

See `profile/certifications/aws-solutions-architect-professional.md`.

### `basics.md`

The one file with no directory and no `type` key — its path is its type. Frontmatter
maps directly to `ResumeBasics`: `fullName`, `headline`, `email`, `phone`, `location`,
`website`, `linkedin`, `github`. The body is the default summary, used whenever a
tailoring run doesn't emit a `setSummary` command.

```markdown
---
fullName: Jordan Rivera
headline: Senior Backend Engineer
email: jordan.rivera@example.com
phone: +1 (206) 555-0142
location: Seattle, WA
website: https://jordanrivera.dev
linkedin: https://linkedin.com/in/jordan-rivera-dev
github: https://github.com/jordanrivera
---

Backend engineer with eight years building distributed systems in C# and Go...
```

## Skills are derived, not authored

There is deliberately **no `profile/skills/` directory and no skills markdown file.**
You do not author a skills list anywhere in the knowledge base.

Skill groups are synthesized by `ResumeBuilder` from the `tech:` frontmatter of every
experience and project entry: each value is normalized through `SkillNormalizer` and
looked up in `ISkillTaxonomy`, then bucketed into a `SkillGroup` per taxonomy category —
`languages`, `frameworks`, `datastores`, `cloud`, `practices`, `tools`, `soft` — emitted
in that fixed order. Skills are sorted alphabetically by display name within each group,
and a category with nothing in it is omitted entirely rather than rendered empty.
Anything the taxonomy can't canonicalize is collected into a trailing `skl:other` group
labelled "Other" instead of being dropped. Skill IDs follow the same grammar as
everything else: `skl:{category}#{canonical}` — e.g. a `tech: [C#]` value on an
experience entry normalizes into `skl:languages#csharp`.

For example, `profile/experience/nimbus-systems.md`'s `tech: [C#, .NET, PostgreSQL,
Kubernetes, Kafka]` and `profile/projects/flowmesh.md`'s `tech: [Go, Redis, gRPC]`
together contribute toward `skl:languages` (C#, Go), `skl:datastores` (PostgreSQL,
Redis), `skl:cloud` (Kubernetes), and so on — with no skills file anywhere claiming
those categories directly.

The rationale is that a hand-maintained skills list drifts out of sync with the work it
is supposed to summarize — it's trivially easy to leave "Rust" on a resume for three
years after the one project that used it was deleted. Deriving skill groups from `tech:`
tags instead means a skill can only appear on the resume if some entry actually
evidences it, which is also what makes `CoverageAnalyzer`'s evidence links meaningful:
`skl:languages#go` being covered means an actual experience or project bullet backs it,
not that someone typed "Go" into a list once and forgot about it. A skill group's
`Included` flag and an individual skill's `Emphasized` flag are then set per tailoring
run by the `include`/`exclude`/`emphasizeSkills` commands — see
`docs/tailoring-commands.md` — not edited in the knowledge base.

## Bullets and variants

Top-level `-` list items in the body are bullets — one accomplishment each. An `-` item
indented two spaces under a bullet is a **variant**: an alternate phrasing of the exact
same accomplishment, not a new one.

```markdown
- Cut p99 checkout latency from 840ms to 120ms by replacing a serial fan-out with a
  bounded parallel scatter-gather over 6 downstream services.
  - Rebuilt checkout fan-out as a bounded scatter-gather, cutting p99 from 840ms to 120ms.
- Led migration of 40+ services from .NET 6 to .NET 8, retiring 12k lines of shim code.
```

This parses to one `Bullet` (`exp:acme-corp#0`) with `Text` set to the parent line and
`Variants` containing the one indented line, followed by a second `Bullet`
(`exp:acme-corp#1`) with no variants. Ordinals are 0-based and assigned by position in
the file, top to bottom, counting only top-level bullets — variants don't get their own
ID, they're addressed as `SelectVariantCommand { Target, VariantIndex }` against their
parent.

Write variants for accomplishments that read differently depending on the audience: a
version that leads with the number, a version that leads with the technology, a shorter
version for a tight one-page layout. The tailoring engine can swap to a variant for
zero generation tokens (`selectVariant`), which is why it's cheaper and preferred over
asking the model to rewrite the bullet outright (see `docs/tailoring-commands.md`).

## How slugs become IDs

```
profile/experience/acme-corp.md   →  exp:acme-corp
profile/experience/acme-corp.md, bullet 3 (0-based)  →  exp:acme-corp#2
profile/projects/graph-runner.md, bullet 1  →  prj:graph-runner#0
profile/basics.md  →  sum   (the summary block has no slug)
```

`kind` comes from the parent directory (`experience` → `exp`, `projects` → `prj`,
`education` → `edu`, `certifications` → `cert`); `slug` is the filename without
extension, lowercase `[a-z0-9-]+`. Because the ID is derived from the filename and not
stored in the file, two files can never collide on slug within the same kind — the
filesystem already enforces uniqueness for you.

## GitHub import and hand edits

`POST /api/knowledge/import/github` fetches a repository's README and metadata and
writes a `projects/*.md` file with `source: github` and a `stars` count. That marker is
the contract between the importer and you: **`source: github` means this file is safe
to regenerate.** Re-running import on the same repo overwrites the file. If you've
hand-edited a bullet in an imported file and want to keep the edit, change `source` to
`manual` (or just delete the key) — the importer treats anything without
`source: github` as yours and refuses to overwrite it, surfacing a conflict in the
`GitHubImportResult` instead.

Files you write yourself never carry `source: github`, so they're never touched by
re-import.

## Malformed files

A file that fails to parse — bad YAML, a `type` the reader doesn't recognize, a date
that doesn't match `yyyy-MM` / `yyyy-MM-dd` / `yyyy` / `present` — produces a
`KnowledgeBaseDiagnostic { File, Line, Message, Severity }` and is **skipped**, not
fatal. One bad file in `profile/` never prevents the rest of the knowledge base from
loading; the diagnostic surfaces in `GET /api/profile` so the `/knowledge` list in the
frontend can flag it without taking the app down.

## Round-trip stability

Reading a file and writing it back out — with no changes — produces a byte-identical
file. Frontmatter keys are emitted in the order documented in the tables above, not
alphabetically and not in file-original order if it differed. This matters because the
`/knowledge` editor in the frontend reads a file, may modify one field through a form,
and writes the whole file back; round-trip stability is what keeps that from turning
into unrelated diff noise in `git`.
