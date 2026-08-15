# Contributing

## Before you start

`docs/CONTRACTS.md` is the frozen source of truth for anything that crosses a component
boundary — the resume model, the tailoring commands, the HTTP API, the autofill
contracts. If your change needs a type in there to be different, propose the contract
change first (open an issue); don't drift backend, frontend, and extension apart by
updating one and not the others.

## Running the project

Prerequisites: .NET 10 SDK, Node 20+.

```bash
# Backend — runs on http://localhost:5217, applies migrations automatically in Development
dotnet run --project backend/src/ResumeForge.Api

# Frontend — runs on http://localhost:5173, proxies /api to the backend above
cd frontend && npm install && npm run dev

# Extension
cd extension && npm install && npm run build
```

No API key is required. Without `ANTHROPIC_API_KEY` set, the backend registers
`HeuristicLanguageModel` automatically and the whole tailoring pipeline runs end to end
against the sample data in `profile/`.

## Running tests

```bash
# Backend
dotnet test

# Frontend
cd frontend && npm run typecheck && npm run lint && npm run test -- --run

# Extension
cd extension && npm run typecheck && npm run test -- --run
```

CI (`.github/workflows/ci.yml`) runs all three on every push and pull request to `main`.
`TreatWarningsAsErrors` is on for the backend — a warning on your machine is a failed
build in CI, not a suggestion.

## Commits

[Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`,
`chore:`, `test:`, `refactor:`. Describe what changed and why; don't reference the
tooling used to write the change.

## Adapters are data, not code

If you're adding a job board to the extension's autofill cascade
(`extension/src/adapters/*.ts`), keep it declarative: a match predicate and a canonical
key → CSS selector map, nothing else. No per-board scraping logic, no DOM traversal
beyond what a selector expresses. That constraint is what keeps a new adapter a small,
reviewable diff instead of a maintenance burden — see `docs/extension.md` for a worked
example.

## Style

`.editorconfig` at the repo root enforces file-scoped namespaces on the backend (as a
build error) and reasonable defaults everywhere else. See `docs/CONTRACTS.md` §13 for
the full convention list (sealed by default, primary constructors for DI, `TimeProvider`
instead of `DateTime.Now`, `ILogger<T>` instead of `Console.WriteLine`, xUnit v3 +
Shouldly + NSubstitute for tests).
