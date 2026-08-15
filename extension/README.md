# ResumeForge Autofill

A Manifest V3 Chrome extension that fills job application forms from your
ResumeForge profile. The whole design is built around one constraint:
**autofill should cost as close to zero model tokens as possible.**

## The cascade

Every field on a job application form is resolved by trying, in order:

1. **Board adapter** (`src/adapters/*.ts`) — a declarative selector map for
   a specific job board (Greenhouse, Lever, Ashby, SmartRecruiters,
   Workday). Zero tokens, zero guessing — if the selector matches, the field
   is 100% resolved.
2. **Heuristic matcher** (`src/heuristics/`) — scores every still-unresolved
   field against a synonym table for each canonical key, using its label,
   `name`, `id`, `placeholder`, `aria-label`, and `autocomplete` attribute.
   Zero tokens. A match is accepted at confidence ≥ 0.72.
3. **Model fallback** — the fields still unresolved after tiers 1 and 2 are
   batched into a *single* request to `POST /api/autofill/resolve`. The
   response is persisted server-side as a `LearnedFieldMap` keyed by
   `(host, formSignature)`, so the same form costs zero tokens on every
   subsequent visit — the extension checks
   `GET /api/autofill/fieldmap/{host}` before ever reaching tier 3 again.

The on-page overlay shows every planned fill with the tier that resolved it
(adapter / heuristic / learned / model / unresolved) and its confidence, so
the cascade isn't a black box. Nothing is written to the page until you
click **Apply selected**, and the extension never submits a form or clicks
a submit button on your behalf.

### Why `elementId` is a content fingerprint, not a scan order

`LearnedFieldMap.elementToKey` (`src/lib/fieldMapCache.ts`,
`background/service-worker.ts`) is a `Record<elementId, canonicalKey>`
persisted per `(host, formSignature)`. For that cache to be safe to reuse
on a later visit, the *same* `elementId` has to mean the *same field* every
time the form is scanned — not just the same `formSignature`.

`computeFormSignature` (`src/content/scan.ts`) is deliberately
order-independent: it sorts the field tuples before hashing, so the same
logical form matches on every visit regardless of how its fields happen to
be laid out in the DOM that particular time. Job application forms often
render conditionally — a "do you require sponsorship?" follow-up that only
appears after another answer, for instance — so the same set of fields
rendering in a different order between two visits is realistic, not
theoretical.

If `elementId` were assigned from DOM traversal order (a simple incrementing
counter), that order-independence becomes a liability instead of a
convenience: two visits with the same fields in different DOM order
produce the *same* `formSignature` but bind each `elementId` to a
*different* field. The learned-map lookup treats that as a hit and applies
its (host, formSignature) canonical keys straight through — silently
writing a phone number into a salary field, or an email into a "how did you
hear about us" field, and only on the exact reuse path the learned map
exists to serve.

`scanPage` avoids this by deriving `elementId` from field *content*
instead: `rf-` + an FNV-1a hash of `name|id|autocomplete|inputType|label`
(label normalized the same way the heuristic matcher tokenizes it, so
trivial copy/whitespace changes don't invalidate a learned map). The id
only changes when the field itself changes — not when something else on
the page changes what renders before it. Genuinely identical fingerprints
(e.g. two unlabelled inputs with no name or id) are disambiguated by an
occurrence-index suffix among same-fingerprint fields, in DOM order
(`rf-<hash>`, `rf-<hash>-1`, `rf-<hash>-2`, ...) — so residual
order-sensitivity is confined to that genuinely ambiguous case instead of
being the default for every field.

## Build

Requires Node 20+ (developed against Node 25) and npm.

```bash
cd extension
npm install
npm run build
```

`npm run build` type-checks, builds the popup/options/service-worker with
Vite, and bundles the content script separately with esbuild (it must stay
a classic script — MV3 content scripts can't be ES modules). The result
lands in `dist/`.

Other scripts:

```bash
npm run typecheck   # tsc --noEmit only
npm run test         # vitest, watch mode
npm run test -- --run  # vitest, single run
npm run dev           # vite build --watch (popup/options/service-worker only)
```

## Load unpacked

1. `npm run build`
2. Open `chrome://extensions`
3. Enable **Developer mode** (top right)
4. Click **Load unpacked** and select the `extension/dist` directory
5. Pin the extension, then visit a supported job board (or start the
   ResumeForge backend at `http://localhost:5217` first — the popup will
   show a "Backend unreachable" status otherwise, but tiers 1 and 2 still
   work fully offline once a profile has been cached)

After editing source, re-run `npm run build` and click the reload icon on
the extension's card in `chrome://extensions`.

## Adding a new board adapter

**This is a data change, not a code change.** A board adapter is a plain
object — no imperative DOM code, no control flow, nothing that executes.
The filler (`src/content/fill.ts`) is the only module that ever touches the
page; adapters just tell it where to look.

To add support for a new board:

1. Create `src/adapters/<board>.ts` exporting a `BoardAdapter`:

   ```ts
   import type { BoardAdapter } from './types';

   export const myBoardAdapter: BoardAdapter = {
     id: 'myboard',
     hostPatterns: [/(^|\.)jobs\.myboard\.com$/i],
     fields: {
       firstName: { selector: 'input[name="first_name"]', kind: 'input' },
       email: { selector: 'input[name="email"]', kind: 'input' }
       // ...one entry per canonical key this board exposes with a stable selector
     },
     fileFields: {
       resume: { selector: 'input[type="file"][name="resume"]', documentKind: 'resume' }
     },
     notes: 'Anything future maintainers should know — dynamic ids, quirks, assumptions.'
   };
   ```

2. Register it in `src/adapters/index.ts`: import it and add it to
   `adapterRegistry`.
3. Only map fields with a **stable, predictable** selector across postings
   for that board. Free-form custom questions (screening questions a
   company writes per-posting) almost always get dynamic element ids —
   leave those out entirely and let the heuristic matcher or the model
   fallback handle them. A wrong static mapping is worse than no mapping.
4. `FieldSelector.optionMatch` is available for `radio`/`select` fields
   where you need to pick an option by a rule rather than by fuzzy text
   matching (rare — most boards' own labels are already good enough for
   the filler's built-in fuzzy matcher in `fill.ts`).
5. Add a couple of assertions to `tests/adapters.test.ts` confirming
   `matchAdapter` picks up the new host.

You do **not** need to touch `content/fill.ts`, `content/scan.ts`,
`content/overlay.ts`, or the heuristic matcher — those are board-agnostic by
design. If you find yourself writing an `if (adapter.id === 'myboard')`
anywhere outside `adapters/`, that's a sign the logic belongs in the
adapter's data instead.

## Canonical field keys

The full set of keys this extension and the backend agree on lives in
`src/contracts.ts` (`CanonicalKey`), mirroring `docs/CONTRACTS.md` §10
exactly. If a board or a synonym needs a key that isn't in that list, the
list has to change in the backend and this file together — it's a closed
set on purpose, since the model fallback's output is validated against it.

## Project layout

```
src/
  contracts.ts        TypeScript mirror of docs/CONTRACTS.md §10
  messages.ts          runtime message protocol (content <-> background <-> popup)
  adapters/             one module per board + registry/matchAdapter
  heuristics/           synonym table + scoring (tier 2)
  content/
    scan.ts              walks the page, builds FieldDescriptors, computes formSignature
    fill.ts              applies resolved values; the no-overwrite / no-submit rules live here
    overlay.ts            Shadow-DOM review panel
    index.ts               content script entry — wires scan -> tiers 1-3 -> overlay -> fill
  background/
    service-worker.ts     the only module that talks to the backend
  popup/, options/         extension UI
  lib/                     hash (FNV-1a), settings, and cache helpers
tests/                    vitest unit tests
```
