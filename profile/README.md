# Sample profile data

Everything under `profile/` is fictional. "Jordan Rivera" is not a real person, and the
companies, projects, and metrics are invented for demonstration. The data exists so a
clone of this repo can run the full tailoring pipeline immediately, with no setup: point
`/api/tailor` at a job posting and the base resume is built from these files.

## Replacing it with your own

1. Delete or edit the files under `experience/`, `projects/`, `education/`, and
   `certifications/`, and rewrite `basics.md` with your own details.
2. Keep the frontmatter keys and date format (`yyyy-MM`, `yyyy-MM-dd`, `yyyy`, or
   `present`) — see `docs/knowledge-base.md` for the full field reference.
3. The filename becomes the entity's slug (`experience/acme-corp.md` → `exp:acme-corp`),
   so rename files rather than editing an `id` field — there isn't one.
4. Write bullets as top-level `-` list items. An indented `-` under a bullet is an
   alternate phrasing the tailoring engine can pick instead of the original — write one
   wherever you'd naturally rephrase an accomplishment for a different audience.
5. For projects, GitHub import (`POST /api/knowledge/import/github`) will write files
   with `source: github` for you. Files you write by hand should use `source: manual` (or
   omit `source` for experience/education/certification entries, which don't carry it).
6. Run `dotnet run --project backend/src/ResumeForge.Api` and open the frontend — the
   dashboard and `/knowledge` list should reflect your changes as soon as the files are
   saved; nothing needs to be re-imported.

`ResumeForge:ProfileRoot` in configuration controls where this directory is read from, if
you'd rather keep your real profile outside the repo entirely.
