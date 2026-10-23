# 8. Cross-cutting concepts

## Security

Security is the first quality goal. The operator-facing view (threat model, hardening checklist, security log) is in the [security guide](../security.md); this section describes how the code implements it.

### Authentication and sessions

- **Passwords:** ASP.NET Core Identity's `PasswordHasher` (PBKDF2-HMAC-SHA512, 210,000 iterations, random salt). Older hashes are upgraded on the next sign-in. 12 to 1024 characters, no composition rules (NIST SP 800-63B). Overlong input is refused before hashing.
- **Unknown accounts** take the same time as wrong passwords (a dummy hash is computed) and get the same message.
- **Brute force:** a fixed-window rate limit per client address (`LoginRateLimitMax`, 30 per 15 minutes) and `LoginThrottle`, which locks an account for 15 minutes after `LoginMaxFailures` (10) failures from any address. The lock is in memory and also counts unknown emails, so it doesn't reveal which accounts exist.
- **Sessions** are rows in `sessions` holding the SHA-256 hash of a 32-byte random token. They slide (`SessionLifetimeDays`, 30) but end at the latest `SessionMaxAgeDays` (90) after sign-in. A password change ends the user's other sessions; an administrator's reset ends all of them; deleting a user deletes their sessions.
- **Cookie:** `__Host-notes_session` (Secure, Path=/) over HTTPS or `notes_session` (Path=/api) over HTTP; always `HttpOnly` and `SameSite=Strict`. Two session cookies at once are refused. API clients may send `Authorization: Bearer <token>`; only requests without `Origin` get the token in the login response.

### Authorization and isolation

- Every endpoint except health and login requires a session; `/api/admin/*` additionally the `admin` role, read from the database on every request (a demoted administrator loses access at once).
- **Every query is scoped to the signed-in user.** Services look items up by `(userId, id)`, so another user's id behaves exactly like a missing one (404). Attachments live in per-user folders.
- Ids from the URL are validated (`Ids.TryParse`, 32 hex characters) before they reach a query or a path.

### Request protection

| Mechanism | Where |
| :--- | :--- |
| CSRF: `SameSite=Strict` plus `Origin` must be the app's own (request host or `PublicUrl`) for POST/PUT/PATCH/DELETE; requests without `Origin` pass only without the cookie | `OriginCheckMiddleware` |
| Content Security Policy (`default-src 'self'`, `script-src 'self'`, `object-src 'none'`, `frame-ancestors 'none'`, images from `'self' data: blob:` and optionally `https:`), `nosniff`, `X-Frame-Options`, `Referrer-Policy: no-referrer`, COOP/CORP, `Permissions-Policy`, HSTS over HTTPS, `Cache-Control: no-store` on the API | `SecurityHeadersMiddleware` |
| Rate limits per address, 429 with `Retry-After` | `RateLimits` |
| Request body limit (`MaxUploadMb`), no `Server` header | Kestrel options in `Program.cs` |
| Forwarded headers only from trusted proxies | `TrustedProxies`, `ForwardedHeadersOptions` |
| Attachments: `nosniff`, `sandbox` CSP, `attachment` disposition unless inline-safe, cleaned file names, stored under ids | `ResourceTypes`, resource endpoint |
| Note rendering: sanitized Markdown/HTML (rehype-sanitize), `:/id` resolved through the API | `MarkdownRenderer` |

### Supply chain

Dependencies are pinned (NuGet `packages.lock.json` with `--locked-mode`, npm `package-lock.json`, base images by digest, actions by commit SHA). NuGet audit fails the build on known vulnerabilities; CI runs `npm audit`, dependency review, CodeQL and an image scan; Dependabot proposes updates weekly. Releases ship SBOMs and a signed provenance attestation. See [CRA compliance](../cra.md).

## Persistence

- EF Core with Npgsql and snake_case names. Schema changes are EF migrations in `Data/Migrations` (raw SQL where EF can't express something, such as the `pg_trgm` indexes and the case-insensitive tag index). Migrations run on start; they are never edited once released.
- Times are stored as `timestamptz` truncated to milliseconds (`Times.Now`), so values survive the round trip through the API unchanged.
- Services use `AsNoTracking` for reads and set-based `ExecuteDelete`/`ExecuteUpdate` for bulk changes (trash, purge, session cleanup).

## Error handling

Services throw `ApiException` with a status and a message meant for the user (optionally `details`, e.g. the current note on a 409). `ErrorHandlingMiddleware` turns it into `{ error, status, details? }`. Malformed JSON becomes a 400 (`ThrowOnBadRequest`), oversized bodies a 413. Anything else is logged and answered with a generic 500, never with internals.

## Logging

Standard `ILogger` to the console. Security events have their own category, `Notes.Security`, with stable event ids (see the [security guide](../security.md#security-log)); they never contain passwords or tokens. The default level is Information for the app and Warning for ASP.NET Core and EF Core.

## Versions of notes

`VersionService.SnapshotBeforeChangeAsync` runs before every change of title or body. It stores the previous state unless the newest version is younger than `VersionIntervalMinutes`, the note was empty, or the content equals the newest version. `VersionPruning` deletes versions older than `VersionRetentionDays` (0 keeps all).

## Markdown dialect and links

Notes uses Joplin's Markdown: GitHub-flavored Markdown plus KaTeX math, Mermaid, `==mark==`, `++insert++`, `~sub~`, `^sup^`. Links to other notes and attachments are `:/<32-hex id>`. HTML notes (`markup_language = 2`) are rendered as sanitized HTML. The export rewrites `:/` links to relative file paths.

## User interface

- Same components and design as Joplin Web: Tailwind CSS 4 with the tokens in `index.css` (`bg-background`, `text-muted-foreground`, …), shadcn/ui primitives, light and dark theme, layouts for phone (one pane at a time) and desktop (three panes).
- Server state only through TanStack Query hooks; mutations write the server's answer into the affected caches (the note, lists) and invalidate the rest (counts, tags).
- Unsaved text is kept as drafts in `localStorage`, owned by the signed-in account and dropped on sign-out.
- Only `lib/api.ts` knows the backend, so the UI can move into a shared library later.

## Testing

| Level | Tooling | Scope |
| :--- | :--- | :--- |
| Backend integration | xUnit v3 on Microsoft Testing Platform, `WebApplicationFactory`, a real PostgreSQL database per test class, `FakeTimeProvider` | All endpoints, isolation between users, sessions and locks, versions, search, export, security headers |
| Backend unit | xUnit | Parsers and helpers (search syntax, previews, MIME types, file names) |
| Client | Vitest, Testing Library, jsdom, a stubbed `fetch` | Hooks, components, pages, dialogs |
| End to end | Playwright against the real backend and the demo account | Sign-in, browsing, editing, tagging, search, trash, history, attachments, export, phone layout |
