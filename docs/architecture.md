# Architecture

ResumeForge is a standard layered .NET backend (Domain → Application → Infrastructure →
Api) behind a React frontend and a browser extension. This page covers the layering
rule, why the knowledge base rather than the database is the source of truth, what the
database is actually for, and walks one full request end to end.

## Layers

```
backend/src/
├── ResumeForge.Domain          # records, IDs, no dependencies on anything else in the repo
├── ResumeForge.Application     # use cases, ports (interfaces), the graph engine
├── ResumeForge.Infrastructure  # EF Core, markdown I/O, PDF rendering, language model clients
└── ResumeForge.Api             # ASP.NET Core minimal API endpoints, composition root
```

**The dependency rule: dependencies point inward, always.**

- `Domain` references nothing else in the solution. It's the `ResumeDocument` model, the
  `EntityId` grammar, and the knowledge-base record types — pure data, no I/O.
- `Application` references `Domain` only. It defines the use cases (analyze a job
  posting, score candidates, validate and execute tailoring commands, run the DAG) and
  the **ports** those use cases need — `IKnowledgeBaseReader`, `ILanguageModel`,
  `ICommandValidator`, `IFabricationGuard` — as interfaces. It has no idea EF Core or
  Markdig exist.
- `Infrastructure` references `Application` (to implement its ports) and `Domain`. This
  is where `ResumeForgeDbContext`, the Markdig-based knowledge base reader/writer, the
  QuestPDF renderer, and the `AnthropicLanguageModel` / `HeuristicLanguageModel` /
  `CachingLanguageModel` implementations live.
- `Api` references all three, wires the DI container, and is the only layer that knows
  concrete implementations exist. Everything below it programs against interfaces.

The payoff: `Application` can be unit tested with `NSubstitute` fakes for every port and
never touch a filesystem, a database, or a network call. `ResumeForge.Application.Tests`
and `ResumeForge.Domain.Tests` should never need a running SQLite file.

## Markdown is the source of truth, not the database

The knowledge base — every role, project, degree, certification — lives in
`profile/*.md`, not in a table. Two reasons this is a real design decision and not just
"files are easy":

1. **It's a personal record, not application state.** A resume is something you own for
   a decade across jobs, not something that should only exist inside one app's database.
   Markdown files diff cleanly in `git`, survive `rm -rf` on the database, and can be
   edited in any editor without the app running.
2. **It's what makes the token-saving design possible.** The tailoring pipeline assigns
   every bullet a stable ID derived from `(kind, filename slug, ordinal)` — see
   `docs/CONTRACTS.md` §1. IDs stay stable specifically because they're derived from
   *where a file lives*, not from a database identity column that changes on
   delete/reimport. That stability is what lets the model refer to `exp:acme-corp#2`
   across separate runs and separate processes without the backend restating the bullet
   text to establish which one it means.

The database is not a cache of the knowledge base and never has a copy of it. What it
holds instead — `ResumeForge.Infrastructure.Persistence.ResumeForgeDbContext`, EF Core
10 over SQLite — is exactly the operational and derived state that doesn't belong in a
markdown file:

| Table | What it holds |
| --- | --- |
| `Resumes` | Generated `ResumeDocument` snapshots (base and tailored), as JSON columns |
| `JobPostings` | Fetched/pasted job descriptions |
| `JobAnalyses` | Deterministic JD analysis output, cached against a posting |
| `TailoringRuns` | Commands, diff, coverage, trace — the full result of one `/api/tailor` call |
| `ModelCacheEntries` | `CachingLanguageModel`'s cache, keyed by SHA-256 of `(ModelId, System, User, SchemaName)` |
| `LearnedFieldMaps` | Autofill field maps learned per `(host, formSignature)` |
| `Applications` | The application tracker board |

Complex sub-objects (`ResumeDocument`, `TailoringResult`, etc.) are stored as JSON
columns via `.HasConversion` rather than normalized — they're written once, read whole,
and never queried by internal field, so normalizing them would add join complexity for
no benefit. Migrations are committed to the repo and applied automatically on startup in
Development.

## A tailoring request, end to end

`POST /api/tailor { jobId, baseResumeId?, maxRewrites?, dryRun? }` →

1. **Api** deserializes `TailorRequest`, resolves the base resume (or the current one if
   `baseResumeId` is null), and hands both to the tailoring use case in `Application`.
2. **Application** builds a `GraphBuilder` graph (`TailoringGraphFactory`) and runs it
   through the DAG executor described in `docs/graph-engine.md`. In order of what
   becomes ready to run:
   - `fetch-jd` and `load-kb` start immediately (no dependencies).
   - `analyze-jd` runs the deterministic JD analyzer over the fetched posting text —
     sentence segmentation, skill taxonomy matching, mandatory/optional classification.
     No model call.
   - `build-base` assembles the canonical `ResumeDocument` from the knowledge base.
   - `score-experience`, `score-projects`, `score-skills` run **concurrently** — each is
     BM25 plus a skill-overlap and recency term over one section, using
     `analyze-jd` and `build-base`'s output.
   - `build-brief` merges the three scored candidate sets into the compact brief the
     model will see: requirement text and candidate IDs with truncated bullet text, no
     full resume prose.
   - `propose-commands` sends that brief to `ILanguageModel.CompleteAsync` — either
     `AnthropicLanguageModel` or, with no API key present, `HeuristicLanguageModel` —
     and gets back a `TailorCommand[]`, typically under 600 tokens.
   - `validate-commands` runs `ICommandValidator` against the raw knowledge base and the
     proposed commands: every ID resolves, `RewriteCommand` text passes the
     `IFabricationGuard` and the 300-character/single-line/rewrite-cap rules.
   - `verify-fabrication` and `verify-coverage` run **concurrently** with each other —
     both deterministic, both downstream of validation only.
   - `execute-commands` applies the accepted commands to the canonical document,
     producing the tailored `ResumeDocument` and a `ResumeDiffEntry[]`.
   - `render` runs last, after all three of the above.
3. **Application** returns a `TailoringResult` — the tailored document, the diff,
   `CommandValidationResult` (accepted and rejected commands with reasons), the
   `CoverageReport`, `TokenUsage`, and the full `GraphNodeTrace[]` — back to **Api**.
4. **Api** persists the run (unless `DryRun: true`) to `TailoringRuns` and returns the
   `TailoringResult` as JSON.
5. Separately, `POST /api/render/{resumeId} { format }` takes a `ResumeDocument` id and
   renders it — QuestPDF for `pdf`, a template for `html`/`md` — and streams the file
   back. Rendering is not part of the tailoring graph itself; `render` in the graph
   produces the document in memory, and the file bytes are produced on demand by the
   render endpoint so the same tailored document can be exported in more than one format
   without re-running tailoring.

The React app's `/tailor` route calls `GET /api/tailor/{runId}/trace` (or reads the
trace embedded in the initial response) to draw the live pipeline view — each node's
`Status`, `Duration`, and token counts, exactly as recorded in `GraphNodeTrace`.

See `docs/graph-engine.md` for how the executor decides what runs concurrently and how
it isolates a failing node, and `docs/tailoring-commands.md` for the full command
protocol and a worked JD → commands → diff example.
