## What and why

<!-- What changes, and why. Link the issue: "Closes #123". -->

## Checklist

- [ ] `cd backend && dotnet build && dotnet test`
- [ ] `npm --prefix client run lint && npm --prefix client run build && npm --prefix client test` (and `test:e2e` for user flows)
- [ ] UI changes checked against the demo in light and dark mode and at phone width (~390 px)
- [ ] New or changed endpoints have a backend test; security changes a test in `SecurityTests`
- [ ] Docs: README features, `CHANGELOG.md` (*Unreleased*, breaking/security marked), arc42 section, `docs/security.md` / `docs/cra.md`, screenshots
- [ ] No secrets, `.env` or real server URLs; new dependencies only with an allowed license and updated lock files
