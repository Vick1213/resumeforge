# The browser extension: autofill

The extension (`extension/`, Manifest V3) fills job application forms from your
ResumeForge profile. It never touches the model unless it has to, and it never submits
anything on its own. This page covers the resolution cascade, the canonical field keys,
how to add a new job board adapter, the learned-field-map lifecycle, and the safety
rules that are non-negotiable regardless of how a field got resolved.

## The three-tier cascade

Resolving a form's fields is the extension's own token-saving story, the same idea as
tailoring commands applied to a different problem: spend model tokens only on what
genuinely can't be solved without them, and remember the answer so you never pay twice.

```
1. Board adapter        declarative selector map, zero tokens, instant
2. Heuristic matcher     label/name/aria scoring, zero tokens, instant
3. Model fallback        one batched call for whatever's left, cached forever after
```

### Tier 1 — board adapters

`extension/src/adapters/*.ts` ship a declarative selector map per known applicant
tracking system — Greenhouse, Lever, Ashby, Workday, SmartRecruiters. An adapter matches
a page by hostname/URL shape and maps canonical field keys straight to CSS selectors.
No text scoring, no model call — if the adapter matches and the selector is on the page,
the field is resolved with certainty.

### Tier 2 — heuristic matcher

For fields the matched adapter didn't cover, or on a board with no adapter at all, the
heuristic matcher scores every remaining form field against the canonical key synonym
table using its label text, `name`/`id`/`autocomplete` attributes, `placeholder`, and
`aria-label`, via normalized token overlap. A match is accepted at confidence **≥ 0.72**;
below that, the field is left for tier 3 rather than guessed at.

### Tier 3 — model fallback

Whatever is still unresolved after tiers 1 and 2 — usually a handful of fields on an
unfamiliar board, or a genuinely ambiguous label — is batched into **one** request:
`POST /api/autofill/resolve`. Not one request per field; one request for the whole
leftover set. The response is a `FieldResolution[]`, and it's persisted as a
`LearnedFieldMap` keyed by `(host, formSignature)` — so the same form, the next time you
open it, resolves entirely at tier 1-equivalent speed with zero model tokens spent. The
model is a one-time cost per distinct form shape, not a recurring one.

## Canonical field keys

This is a closed set. The extension and the backend agree on it exactly — it's mirrored
in `ResumeForge.Application.Autofill` and `extension/src/contracts.ts`, and nothing
resolves to a key outside this list.

| Key | Typical source |
| --- | --- |
| `firstName`, `lastName`, `fullName`, `preferredName` | `ResumeBasics.FullName`, split or as-is |
| `email`, `phone` | `ResumeBasics.Email`, `.Phone` |
| `addressLine1`, `addressLine2`, `city`, `state`, `postalCode`, `country` | user-entered in `/settings`, not derived from the resume model |
| `linkedin`, `github`, `portfolio`, `website` | `ResumeBasics.LinkedIn`, `.GitHub`, `.Website` |
| `currentCompany`, `currentTitle` | most recent `ExperienceEntry` |
| `yearsExperience` | derived from experience date ranges |
| `workAuthorization`, `requiresSponsorship`, `willingToRelocate` | user-entered preference |
| `noticePeriod`, `desiredSalary`, `availableStartDate` | user-entered preference |
| `gender`, `ethnicity`, `veteranStatus`, `disabilityStatus` | user-entered, optional EEO fields |
| `howDidYouHear`, `referredBy` | user-entered per application, often left blank |

`AutofillProfile.Fields` is `IReadOnlyDictionary<string, string?>` keyed exactly by
these strings — a value of `null` (or an absent key) means "no value to fill," and the
extension leaves that field alone rather than filling it with an empty string.

## Writing a board adapter

An adapter is data, not logic — a set of host patterns and a selector map. The shape,
from `extension/src/adapters/types.ts`:

```typescript
export interface FieldSelector {
  selector: string;                              // CSS selector
  kind: 'input' | 'select' | 'radio' | 'textarea' | 'file';
  optionMatch?: (optionText: string) => boolean;  // for radio/select chosen by label text
}

export interface FileFieldSelector {
  selector: string;
  documentKind: 'resume' | 'coverLetter';         // which AutofillDocument to attach
}

export interface BoardAdapter {
  id: string;
  hostPatterns: RegExp[];
  fields: Partial<Record<CanonicalKey, FieldSelector>>;
  fileFields?: Partial<Record<'resume' | 'coverLetter', FileFieldSelector>>;
  notes?: string;
}
```

`extension/src/adapters/greenhouse.ts` is a complete, real one — annotated here:

```typescript
export const greenhouseAdapter: BoardAdapter = {
  id: 'greenhouse',

  // Matched against the tab's hostname before any selector is tried. Two
  // patterns because Greenhouse serves both its legacy embedded form and the
  // newer "Job Board 2.0" templates from different subdomains.
  hostPatterns: [/(^|\.)boards\.greenhouse\.io$/i, /(^|\.)job-boards\.greenhouse\.io$/i],

  // Canonical key -> selector. Only standard candidate fields are stable
  // enough across postings to map declaratively.
  fields: {
    firstName: { selector: '#first_name, input[name="job_application[first_name]"], input[name="first_name"]', kind: 'input' },
    lastName:  { selector: '#last_name, input[name="job_application[last_name]"], input[name="last_name"]', kind: 'input' },
    email:     { selector: '#email, input[name="job_application[email]"], input[name="email"]', kind: 'input' },
    phone:     { selector: '#phone, input[name="job_application[phone]"], input[name="phone"]', kind: 'input' },
    linkedin:  { selector: 'input[name="job_application[urls][LinkedIn]"], input[name*="urls][LinkedIn" i], #linkedin', kind: 'input' },
    website:   { selector: 'input[name="job_application[urls][Website]"], input[name*="urls][Website" i]', kind: 'input' },
  },

  // File uploads are addressed separately from text fields, keyed by
  // AutofillDocument.kind rather than a CanonicalKey.
  fileFields: {
    resume: { selector: '#resume, input[name="job_application[resume]"], input[type="file"][id*="resume" i]', documentKind: 'resume' },
    coverLetter: { selector: '#cover_letter, input[name="job_application[cover_letter]"], input[type="file"][id*="cover_letter" i]', documentKind: 'coverLetter' },
  },

  // Free-form screening questions ("Why do you want to work here?") get a
  // dynamic id per posting and can't be mapped declaratively — deliberately
  // left out, so they fall through to tier 2 or tier 3.
  notes: 'Custom screening questions resolve via the heuristic matcher or the model fallback.',
};
```

Registration is a plain array in `extension/src/adapters/index.ts` — the resolver tries
each adapter's `hostPatterns` in order and uses the first match. A canonical key present
in the form but absent from `fields` simply falls through to tier 2 for that one field;
you don't have to map every field to get value from an adapter, and a partial map (as
above — no EEO fields, no custom questions) is the normal, expected shape for an
adapter, not a gap to fill in later.

Keep adapters declarative on purpose: no DOM traversal logic beyond a CSS selector, no
per-board JavaScript hacks. `extension/src/content/fill.ts` is the only module that
interprets a `FieldSelector` and actually touches the DOM — an adapter never does so
itself. That split is what keeps a new board addition a small, reviewable diff (as
`greenhouse.ts`, `lever.ts`, `ashby.ts`, `workday.ts`, and `smartrecruiters.ts` all are)
and keeps the adapter layer a place other people can contribute to without
understanding the rest of the extension.

## The learned-field-map lifecycle

```
1. Tier 3 resolves N leftover fields in one model call.
2. POST /api/autofill/fieldmap persists { host, formSignature, elementToKey, learnedAt }.
3. Next visit to a form with the same (host, formSignature): GET /api/autofill/fieldmap/{host}
   returns the map. Matching entries resolve instantly — tier 3 is skipped entirely.
4. HitCount increments on each reuse, so the map's actual value is visible in the UI/DB
   rather than assumed.
```

`formSignature` is a stable hash of the field set on the page — its element order,
input types, and available labels — not of the page URL. This is what makes the cache
survive `/jobs/1234` becoming `/jobs/5678` for the same board: two postings on the same
ATS with the same underlying form generate the same signature, so the very first
Greenhouse posting you fill out with the model teaches the extension every future
Greenhouse posting shaped the same way, not just that one.

If a board changes its form shape (new field added, an existing field renamed), the
signature changes, the cached map misses, and the cascade runs fresh for that form —
falling back through tiers 1 and 2 first, and only re-spending model tokens on whatever
tier 2 still can't place with confidence.

## Safety rules

These hold regardless of which tier resolved a field:

- **Never auto-submit.** The extension fills fields; it never clicks Submit, Apply, or
  Continue. That action is always the user's.
- **Never overwrite a field the user has already typed into.** If a field has a
  non-empty value when the extension runs, it's left untouched — the extension doesn't
  know whether that value is deliberate or a partial fill, and guessing wrong there is
  worse than not filling the field at all.
- **Everything is previewed before it's applied.** Every proposed fill is shown in a
  per-field accept/reject overlay before it touches the page's DOM. A user can reject one
  field's suggestion (say, a low-confidence `howDidYouHear` guess) while accepting the
  rest, and a rejected suggestion never gets silently retried.

None of this is configurable from a board adapter or from the model response — it's
enforced by the extension's fill controller itself, so a bad adapter, a bad heuristic
match, or a bad model resolution can produce a *wrong* suggestion but never an
*unreviewed* one.
