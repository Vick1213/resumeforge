## What

<!-- One or two sentences: what does this change, and why. -->

## Where

<!-- Which layer(s)? Domain / Application / Infrastructure / Api / frontend / extension / docs -->

## How was this tested

- [ ] `dotnet test` passes locally
- [ ] `npm run typecheck && npm run lint && npm run test -- --run` passes locally (frontend and/or extension, as applicable)
- [ ] Manually exercised the change against the app (describe below, if relevant)

## Contract changes

- [ ] This PR does **not** change anything in `docs/CONTRACTS.md`
- [ ] This PR **does** change `docs/CONTRACTS.md` — every consumer (backend, frontend, extension) has been updated to match, in this PR or linked follow-ups: <!-- links -->

## Checklist

- [ ] Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/)
- [ ] No new build warnings (`TreatWarningsAsErrors` is on — a warning locally is a failed build in CI)
- [ ] Public types have an XML doc comment (backend) or are otherwise self-explanatory
