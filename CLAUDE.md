# Notes

Self-hosted Markdown notes app: notebooks, tags, search, attachments, to-dos, version history, multiple users (an administrator adds them, no public registration). A React SPA talks to a .NET 10 backend that stores everything in PostgreSQL. Deployed as a Docker Compose stack (app + Postgres) in a Proxmox LXC. The UI came from [joplin-web](https://github.com/KamiNeko/joplin-web) and should stay 1:1 with it.

Security is a first-class requirement and the project follows the EU Cyber Resilience Act. Read the architecture documentation ([docs/architecture/](docs/architecture/README.md), arc42) before touching the data model, sessions/security, versions, search, export or the REST API, and [docs/security.md](docs/security.md) plus [docs/cra.md](docs/cra.md) before touching authentication, logging, dependencies, the build or releases.

## Layout

- `backend/` (`Notes.slnx`, `global.json` selects Microsoft Testing Platform for `dotnet test`)
  - `src/Notes.Api/`: minimal APIs + EF Core (Npgsql, snake_case). `Data/` (entities, DbContext, migrations, demo seed), `Auth/`, `Services/`, `Endpoints/ApiEndpoints.cs`, `Infrastructure/`, `Cli/`.
  - `tests/Notes.Api.Tests/`: xUnit v3 integration tests through `NotesApiFactory` (fresh Postgres database per test class, `FakeTimeProvider`).
- `client/`: React 19 + Vite + TypeScript, Tailwind 4, shadcn/ui (`src/components/ui/` is generated), TanStack Query, CodeMirror 6. Import alias `@/` → `client/src/`. Tests next to the code as `*.test.ts(x)` (Vitest + Testing Library, jsdom); `src/test/` has the `fetch` stub and `renderWithProviders`. Playwright smoke tests in `client/e2e/`.
- `docker/Dockerfile` (client build → `dotnet publish` → aspnet runtime, client in `wwwroot`), `docker-compose.yml` + `.env.example` (hardened production stack), `.devcontainer/` (SDK, Node 24, Postgres), `.github/workflows/` (`ci.yml`, `codeql.yml`, `scorecard.yml`, `release.yml` on `v*` tags), `.github/dependabot.yml`.
- `docs/`: `architecture/` (arc42, one file per section), `security.md`, `cra.md`, `deployment.md`; `SECURITY.md`, `CONTRIBUTING.md` and `CHANGELOG.md` at the root, issue and PR templates in `.github/`.

## Commands

```bash
cd backend && dotnet build && dotnet test            # needs Postgres (NOTES_TEST_DATABASE, default localhost notes/notes)
dotnet run --project backend/src/Notes.Api           # API :5080, Development seeds demo@notes.test / demo-password
cd backend && dotnet ef migrations add <Name> --project src/Notes.Api --output-dir Data/Migrations   # after `dotnet tool restore`
npm --prefix client run lint
npm --prefix client run build
npm --prefix client test
npm --prefix client run test:e2e                     # starts backend (E2E_DATABASE) + Vite itself
npm --prefix client run dev                          # Vite :5173, proxies /api to API_PROXY_TARGET (default :5080)
npm --prefix client run screenshots                  # README screenshots (backend + client running)
```

Node.js 24 (`.nvmrc`): the client tests fail on Node 22 (jsdom `Blob` in `Response`).

## Rules

- **Per-user isolation.** Every query filters by the signed-in user (`user.UserId()`); look items up by `(userId, id)` and answer a foreign id with 404. New tables get a `user_id` (or reach the user through their parent) and an index.
- Validate ids with `Ids.TryParse` (32 hex characters). Errors are thrown as `ApiException` and become JSON `{ error, status, details? }`.
- Note writes keep conflict detection (`baseUpdatedTime` → 409 with `details.current`) and call `VersionService.SnapshotBeforeChangeAsync` before changing title or body.
- Schema changes need an EF migration (raw SQL for what EF can't express, e.g. trigram indexes). Never edit an applied migration.
- Notes stay plain Markdown compatible with Joplin's dialect (`:/<id>` links to notes and attachments).
- New or changed endpoints need a test in `backend/tests`. Keep the build warning-free (`TreatWarningsAsErrors`).
- Client: server state goes through TanStack Query hooks in `client/src/hooks`; mutations invalidate the affected queries. Only `lib/api.ts` knows the backend, so the UI can later move into a shared library. Style with Tailwind tokens from `index.css`; both themes and phone width must work.
- Match the surrounding code: XML docs / JSDoc on public members, comments only where intent isn't obvious.
- Never commit secrets, real server URLs or `.env`. Use the demo seed for screenshots and tests.
- **Security.** Log security-relevant events through `Infrastructure/SecurityLog.cs` (new event ids, never passwords or tokens) and list them in `docs/security.md`. Keep the security headers, Origin check, rate limits and the container hardening in `docker-compose.yml`. Security changes need a test in `SecurityTests` (or the matching test class).
- **Supply chain.** NuGet restores in locked mode: after changing a package reference run `dotnet restore` and commit the updated `packages.lock.json`. Pin new base images by digest and new GitHub Actions by commit SHA with a `# vX.Y.Z` comment; workflows get read-only `permissions` at the top and write permissions only on the jobs that need them. Don't add dependencies without need, and only with licenses the dependency review allows.
- User-facing changes: update the README feature list, add a `CHANGELOG.md` entry under *Unreleased* (mark breaking changes and security fixes), and refresh screenshots when the UI visibly changed. Keep the arc42 sections current: the REST API and data model in `05-building-block-view.md`, flows in `06`, new decisions as an ADR in `09`. Update `docs/security.md` and `docs/cra.md` when a protection or a gap changes.

## Definition of done

1. `cd backend && dotnet build && dotnet test`
2. `npm --prefix client run lint && npm --prefix client run build && npm --prefix client test` (warnings OK, no errors); for user flows also `npm --prefix client run test:e2e`.
3. UI changes: checked in the browser against the demo, in light + dark and at phone width (~390px).
4. Docs updated as described above.

## Git

Branches `feature/<topic>` or `fix/<topic>` off `main`. Conventional commit subjects (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`, `ci:`). Open PRs against `main`.
