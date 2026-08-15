# Tailoring commands

This is the protocol between the model and the rest of the system: the *only* thing the
model produces during a tailoring run. Everything else — parsing the job posting,
scoring candidates, applying the commands, rendering the document — is deterministic C#.
Full type definitions are in `docs/CONTRACTS.md` §6; this page is the practical
reference, with a JSON example for every command and one worked run start to finish.

## Why the surface is this narrow

The model is never shown a resume and never asked to produce one. It's shown a brief —
the job requirements plus a scored candidate list of IDs with truncated text — and it
answers with a short list of commands against those IDs. A typical run's command list is
under 600 tokens, regardless of how long the underlying resume is, because the commands
reference content instead of restating it. See the README for the token-economics
argument in full; this page is about the protocol that makes it possible.

## The commands

All commands share one optional field, `rationale` — a short clause shown in the diff
UI so a human can see *why* the model made a given call, not just *what* it did. JSON on
the wire uses the `"op"` discriminator and camelCase field names per the wire-format
rule in `docs/CONTRACTS.md`.

### `include` / `exclude`

Add or drop a node from the rendered document without touching the knowledge base —
either way, the source markdown is untouched. `include`/`exclude` behave differently
depending on what kind of node they target, and the difference is deliberate:

- **Entries** (`exp:`, `prj:`, `edu:`, `cert:`) and **skill groups** (`skl:languages`,
  not an individual skill) carry an `Included` boolean. Excluding one flips it to
  `false`. The entry stays in the document — the diff, the coverage report, and the UI
  can still refer to it by ID — and renderers simply skip anything with
  `Included == false`.
- **Bullets** (`exp:acme#2`) and **individual skills** (`skl:languages#csharp`) have no
  `Included` flag to flip. Excluding one **physically removes it** from its parent's
  list in the produced document. Nothing is lost from the audit trail: the removal is
  still recorded as a `ResumeDiffEntry` with `Kind = Excluded` and `Before` set to the
  original text.

```json
{ "op": "exclude", "targets": ["exp:cascade-analytics", "prj:tinyorm"], "rationale": "internship and low-relevance side project for a senior role" }
```

```json
{ "op": "include", "targets": ["cert:aws-solutions-architect-professional"], "rationale": "JD lists AWS as a plus" }
```

**Excluding a bullet does not renumber its siblings.** Bullet IDs are assigned once,
from position in the knowledge base file, at build time — never recomputed. If
`exp:acme-corp` has bullets `#0`, `#1`, `#2` and a command excludes `exp:acme-corp#1`,
the rendered entry ends up with two bullets that keep the IDs `#0` and `#2` — there is
no `#1` afterward, and what was `#2` does **not** shift down to become `#1`. This
matters because every command in a run addresses content by ID, including commands
issued *after* an exclude in the same command list: a `selectVariant` or `rewrite`
targeting `exp:acme-corp#2` still means the original third bullet, whether or not an
earlier command in the same run removed `#1`. Renumbering mid-run would silently
retarget every subsequent command at the wrong bullet — the executor never does this.

### `order`

Reorder the children of one parent. Children not listed keep their relative order,
placed after the ones that were listed — so a command doesn't have to enumerate every
child just to move one to the front.

```json
{ "op": "order", "parent": "exp:nimbus-systems", "order": ["exp:nimbus-systems#0", "exp:nimbus-systems#2"], "rationale": "lead with the latency and Kafka bullets" }
```

`parent` can also be `"root"`, to reorder top-level sections (though `setSectionOrder`
below is the more direct way to do that).

### `selectVariant`

Swap a bullet's active text for one of its pre-written variants. This costs zero
generation tokens — the text already exists in the knowledge base — which is why the
validator and the model's own instructions prefer it over `rewrite` whenever a variant
says close enough to what's needed. See `docs/knowledge-base.md` for how variants are
authored.

```json
{ "op": "selectVariant", "target": "exp:nimbus-systems#0", "variantIndex": 0, "rationale": "shorter phrasing fits a trimmed bullet list" }
```

### `rewrite`

The only command that emits prose, and the only one that costs meaningful generation
tokens. Budget-capped and fabrication-checked — see below.

```json
{ "op": "rewrite", "target": "exp:nimbus-systems#2", "text": "Shipped an event-sourced order pipeline on Kafka, taking duplicate-charge incidents from 14/month to zero.", "rationale": "leads with Kafka to match the JD's message-queue requirement" }
```

### `setSummary`

Replaces the top-of-resume summary. There's no `Included` flag to fight with here —
`ResumeDocument.Summary` is a single nullable string, so this simply sets it.

```json
{ "op": "setSummary", "text": "Senior backend engineer with 8 years building distributed, latency-sensitive payment systems in C# and Go.", "rationale": "mirrors the JD's payments and latency framing" }
```

### `emphasizeSkills`

Marks normalized skill names for visual emphasis (typically bold or reordered-first in
the skills section). Note this takes skill **names**, not entity IDs — it's a display
hint, not a structural edit. Since skill groups are derived from `tech:` frontmatter
(see `docs/knowledge-base.md`), a name here only means something if some entry's `tech:`
list actually produced it — there's no separate skills list to draw arbitrary names from.

```json
{ "op": "emphasizeSkills", "skills": ["go", "csharp", "kafka", "kubernetes"], "rationale": "match the JD's mandatory and nice-to-have skills" }
```

### `setSectionOrder`

Reorders the top-level resume sections.

```json
{ "op": "setSectionOrder", "order": ["summary", "skills", "experience", "projects", "education"], "rationale": "JD is skills-heavy, lead with it" }
```

## Validation

`ICommandValidator` runs every proposed command through these checks before anything
touches the canonical document. A failing command is **rejected**, not silently dropped
or clamped to something valid — every rejection is reported back with a reason and
surfaces in the diff UI, because a model that keeps proposing invalid commands is a bug
worth seeing, not something to paper over.

1. Every `Target` / `Parent` / and every entry in `Order` parses as an `EntityId` and
   resolves to a real node in the document. Otherwise: `unknown-target`.
2. `SelectVariantCommand.VariantIndex` is within range for the target bullet's
   `Variants`. Otherwise: `invalid-variant-index`.
3. `RewriteCommand.Text` is ≤ 300 characters, is a single line, and — if the original
   bullet contained a number or a proper noun — shares at least one with the rewrite.
   This is the anti-fabrication check, below. Otherwise: `fabricated-metric`,
   `rewrite-too-long`, or `multi-line-rewrite`.
4. Total accepted `RewriteCommand`s in the run ≤ `TailorOptions.MaxRewrites` (default 6).
   Otherwise: `rewrite-cap-exceeded`.
5. `OrderCommand.Order` has no duplicate entries. Otherwise: `duplicate-order-entry`.

```json
{
  "accepted": [ /* TailorCommand[] */ ],
  "rejected": [
    {
      "command": { "op": "rewrite", "target": "exp:nimbus-systems#0", "text": "Cut checkout latency by over 10x through a total platform rearchitecture." },
      "reason": "Rewrite shares no number or proper noun with the original bullet text.",
      "code": "fabricated-metric"
    }
  ]
}
```

(The command codes above are representative — `docs/CONTRACTS.md` documents the rule
set and gives `unknown-target` / `fabricated-metric` as examples of the `Code` string;
the exact code vocabulary is `ICommandValidator`'s implementation detail as long as
every rejection carries one.)

## The anti-fabrication guard

The model is allowed to **re-angle** a bullet — change what it leads with, tighten the
wording, shift emphasis toward a different phrase — but it is not allowed to **invent**
a metric, a technology, or a claim that wasn't already in the source bullet.
`IFabricationGuard` enforces this mechanically, not by asking the model nicely: if the
original bullet contains a number (`840ms`, `40+`, `14`) or a proper noun (`Kafka`,
`.NET 8`, `Redis`), the rewrite must contain at least one of the same tokens. A rewrite
that turns "cut p99 latency from 840ms to 120ms" into "cut latency by over 10x" fails —
`10x` isn't in the source, even though it's arithmetically close, because the guard
checks token membership, not truth. This is deliberate: the guard has no way to verify a
number is *correct*, only that it isn't new, so it refuses anything it can't verify by
membership rather than trying to be clever about it.

If the original bullet contains no number and no proper noun, the rewrite has nothing to
match against and the check passes trivially — most rewrites in practice are re-angling
bullets that already carry a metric, so this is the less common path.

## Why `selectVariant` beats `rewrite`

Both commands change a bullet's displayed text. Only one of them costs anything:
`selectVariant` picks text that's already been written and reviewed by a human when they
authored the knowledge base entry — zero generation tokens, zero fabrication risk,
because it's not new text at all. `rewrite` asks the model to generate a new sentence,
spends real tokens, and has to clear the anti-fabrication check. The system prompt tells
the model to prefer `selectVariant` whenever an existing variant is close enough, and the
`MaxRewrites` cap (6 per run, configurable via `TailorRequest.MaxRewrites`) backs that
preference with a hard limit rather than relying on the model to police its own restraint.
This is also why writing good variants when you build your knowledge base (see
`docs/knowledge-base.md`) pays off directly: every variant you write ahead of time is one
fewer rewrite the model needs at tailoring time.

## Worked example

**Job posting** (`POST /api/jobs { rawText }`):

> Senior Backend Engineer — Payments Platform
>
> We're scaling our checkout and payments systems and need a senior backend engineer to
> own reliability and latency in the critical request path. Requirements: 5+ years
> backend experience; strong C# or Go; hands-on experience with distributed systems and
> message queues such as Kafka; comfortable owning production incidents. Nice to have:
> Kubernetes, experience leading a service migration, prior mentorship of other
> engineers.

**Deterministic analysis** (`JobAnalysis`, no model call) extracts, among others:

| Requirement | Kind | Mandatory | Weight |
| --- | --- | --- | --- |
| `req:0` 5+ years backend experience | Experience | yes | 0.90 |
| `req:1` strong C# or Go | Skill | yes | 1.00 |
| `req:2` distributed systems and message queues (Kafka) | Skill | yes | 0.95 |
| `req:3` own reliability and latency in the critical request path | Responsibility | yes | 0.80 |
| `req:4` Kubernetes | Skill | no | 0.40 |
| `req:5` leading a service migration | Responsibility | no | 0.50 |
| `req:6` mentorship of other engineers | Responsibility | no | 0.40 |

**Scored candidates** (`CandidateSet`, no model call, top entries against `profile/`):

| ID | Text (truncated) | Score | Matches |
| --- | --- | --- | --- |
| `exp:nimbus-systems#0` | "Cut p99 checkout latency from 840ms to 120ms..." | 0.94 | req:3 |
| `exp:nimbus-systems#2` | "Designed and shipped an event-sourced order pipeline on Kafka..." | 0.88 | req:2, req:3 |
| `prj:flowmesh#0` | "Built a priority-lane scheduler... backpressure..." | 0.85 | req:2 |
| `exp:nimbus-systems#1` | "Led the migration of 40+ services from .NET 6 to .NET 8..." | 0.81 | req:5 |
| `exp:nimbus-systems#4` | "Mentored 3 mid-level engineers..." | 0.62 | req:6 |
| `exp:cascade-analytics#0` | "Built an Airflow DAG that automated..." | 0.11 | — |

**The brief** the model actually receives is this table plus the requirement list —
IDs and truncated text, not the full resume. **The command list it returns**
(well under 600 tokens):

```json
[
  { "op": "exclude", "targets": ["exp:cascade-analytics", "prj:queryviz", "prj:ledgerline", "prj:tinyorm"], "rationale": "low relevance to a senior backend/payments role" },
  { "op": "order", "parent": "exp:nimbus-systems", "order": ["exp:nimbus-systems#0", "exp:nimbus-systems#2", "exp:nimbus-systems#1", "exp:nimbus-systems#4"], "rationale": "lead with latency and Kafka bullets" },
  { "op": "selectVariant", "target": "exp:nimbus-systems#0", "variantIndex": 0, "rationale": "tighter phrasing for a trimmed bullet list" },
  { "op": "rewrite", "target": "exp:nimbus-systems#2", "text": "Shipped an event-sourced order pipeline on Kafka, taking duplicate-charge incidents from 14/month to zero.", "rationale": "leads with Kafka to match the JD's message-queue requirement" },
  { "op": "setSummary", "text": "Senior backend engineer with 8 years building distributed, latency-sensitive payment systems in C# and Go.", "rationale": "mirrors the JD's payments and latency framing" },
  { "op": "emphasizeSkills", "skills": ["go", "csharp", "kafka", "kubernetes"], "rationale": "match the JD's mandatory and nice-to-have skills" }
]
```

The rewrite passes the anti-fabrication check: the original bullet contains `Kafka` and
`14` (per month); the rewrite keeps both, so it shares the required token even though
the wording and structure changed.

**The resulting diff** (`ResumeDiffEntry[]`, after validation and execution):

| EntityId | Kind | Before → After |
| --- | --- | --- |
| `exp:cascade-analytics` | Excluded | included → excluded |
| `prj:queryviz`, `prj:ledgerline`, `prj:tinyorm` | Excluded | included → excluded |
| `exp:nimbus-systems` (bullets) | Reordered | `#0,#1,#2,#4` → `#0,#2,#1,#4` |
| `exp:nimbus-systems#0` | VariantSelected | "Cut p99 checkout latency from 840ms to 120ms by replacing a serial fan-out..." → "Rebuilt checkout fan-out as a bounded scatter-gather, cutting p99 from 840ms to 120ms." |
| `exp:nimbus-systems#2` | Rewritten | "Designed and shipped an event-sourced order pipeline on Kafka, cutting duplicate-charge incidents from 14 per month to zero over two quarters." → "Shipped an event-sourced order pipeline on Kafka, taking duplicate-charge incidents from 14/month to zero." |
| `sum` | SummarySet | (default basics.md summary) → "Senior backend engineer with 8 years building distributed, latency-sensitive payment systems in C# and Go." |
| `go`, `csharp`, `kafka`, `kubernetes` | SkillEmphasized | not emphasized → emphasized |

`CoverageReport.Score` reflects that all four mandatory requirements (`req:0`–`req:3`)
now have at least one `EvidenceIds` entry pointing at an included bullet; the two
optional requirements the model chose not to chase (`req:4` Kubernetes, `req:6`
mentorship — `exp:nimbus-systems#4` was included but reordered last, still counted as
evidence) are reflected as lower-weight, non-blocking gaps rather than failures.
