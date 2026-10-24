# Contributing to Notes

Thanks for helping! Notes is a small hobby project, so the rules are short. They are the same for people and AI assistants ([`CLAUDE.md`](CLAUDE.md) has the full version).

## Security problems

**Never in a public issue.** Report them privately as described in [SECURITY.md](SECURITY.md).

## Issues

- **Bugs:** use the *Bug report* template. Say which version you run (*About Notes* in the account menu, or `GET /api/health`) and how it is deployed. Never paste real notes, passwords, session tokens or your server's address.
- **Ideas:** use the *Feature request* template. Some things are out of scope on purpose (end-to-end encryption, offline mode, sharing; see [ADR-11](docs/architecture/09-architecture-decisions.md)), so please check the architecture decisions first.

## Development

The dev container brings the .NET 10 SDK, Node.js 24 and PostgreSQL 17; the [README](README.md#development) shows how to start backend and client, and the demo login. Read the [architecture documentation](docs/architecture/README.md) before changing the data model, sessions, versions, search, the export or the REST API, and the [security guide](docs/security.md) and [CRA notes](docs/cra.md) before changing authentication, logging, dependencies, the build or releases.

### Rules of thumb

- **Every query is per user.** Look items up by user and id; answer a foreign id with 404.
- **Note writes keep conflict detection** (`baseUpdatedTime` → 409) and save a version before changing title or body.
- **Schema changes need an EF migration**; never edit one that was already applied.
- **New or changed endpoints need a backend test**; security changes a test in `SecurityTests`. The build has to stay free of warnings.
- **Client:** server state goes through the TanStack Query hooks in `client/src/hooks`, only `lib/api.ts` talks to the backend, styles use the Tailwind tokens. Both themes and phone width must work.
- **Dependencies:** only when really needed, with a license the dependency review allows; commit the updated lock files. New GitHub Actions are pinned by commit SHA, base images by digest.
- **Never commit secrets**, `.env` or real server URLs. Use the demo account for tests and screenshots.

### Definition of done

1. `cd backend && dotnet build && dotnet test`
2. `npm --prefix client run lint && npm --prefix client run build && npm --prefix client test`, and for user flows `npm --prefix client run test:e2e`
3. UI changes checked in the browser against the demo, in light and dark mode and at phone width (~390 px)
4. Docs updated: README feature list, a `CHANGELOG.md` entry under *Unreleased* (breaking changes and security fixes marked), screenshots when the UI visibly changed, the matching arc42 section, `docs/security.md` and `docs/cra.md` when a protection changes

## Pull requests

Branch off `main` as `feature/<topic>` or `fix/<topic>`, use [Conventional Commit](https://www.conventionalcommits.org) subjects (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`, `ci:`) and open the pull request against `main`. Keep one topic per pull request; the template has the checklist above. CI runs the tests, CodeQL, the dependency review and an image build.

By contributing you agree that your contribution is licensed under the project's [MIT license](LICENSE).
