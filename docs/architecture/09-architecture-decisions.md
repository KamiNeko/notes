# 9. Architecture decisions

Each decision is recorded with its context, the decision and its consequences. New decisions are appended; a reversed decision is marked *superseded* and links its successor.

## ADR-1: Own backend with its own database

*Accepted, October 2026.*

**Context.** The browser is the only client. A sync server that just stores serialized items offers no query API: a web client on top of it has to keep an in-memory copy of every user's notes, speak a sync protocol and work around conflicts, encryption and missing server-side search.

**Decision.** Build an own backend with its own database that owns the data model and answers queries directly.

**Consequences.** Search, versions, multi-user isolation and export become simple server features. There is no sync with desktop or mobile apps; notes from elsewhere are copied over by hand once.

## ADR-2: .NET 10 minimal APIs and EF Core

*Accepted, October 2026.*

**Context.** The maintainer works with .NET professionally. The API is a few dozen JSON endpoints.

**Decision.** ASP.NET Core 10 minimal APIs with thin handlers, one service class per resource, EF Core 10 with Npgsql. No MediatR, repositories or controllers.

**Consequences.** Little ceremony, everything in one project, easy to debug. Long-term support until November 2028; the next LTS (.NET 12) has to be adopted before that.

## ADR-3: Search in PostgreSQL

*Accepted, October 2026.*

**Context.** Search must support words, phrases, negation and filters (`notebook:`, `tag:`, …) for a few thousand notes per user.

**Decision.** Filter in SQL with `ILIKE` patterns backed by `pg_trgm` GIN indexes; rank and build snippets in C# for at most 500 candidates. No separate search engine.

**Consequences.** No extra service to run or back up; substring matching finds parts of words. No stemming or relevance scoring like a full-text engine; PostgreSQL full-text search (`tsvector`) can be added later if needed.

## ADR-4: Attachments on the filesystem

*Accepted, October 2026.*

**Context.** Attachments can be large; the database should stay small and quick to dump.

**Decision.** Metadata in `resources`, contents as files under `DataDirectory/attachments/<userId>/<id>` on a volume.

**Consequences.** Backups need the database and the volume. Files are streamed without loading them into memory. The two can drift after a crash; missing files answer 404 and are skipped by the export.

## ADR-5: Server-side sessions instead of JWT

*Accepted, October 2026.*

**Context.** The browser is the main client. Sign-out, password changes and account deletion must end sessions immediately.

**Decision.** Opaque random tokens in an `HttpOnly` cookie, stored as SHA-256 hashes in the database, looked up on every request.

**Consequences.** Instant revocation and role changes; no signing keys to manage or rotate. One indexed lookup per request, which is negligible at this scale.

## ADR-6: Keep the existing client's REST API

*Accepted, October 2026.*

**Context.** The client was taken over from an earlier, mature and well-tested web client; its design should stay the same.

**Decision.** The backend answers the routes and JSON shapes that client already used. Parts tied to its old sync backend (conflict copies, E2EE state, server host) were removed from client and API; version history, export and user management were added.

**Consequences.** The client was ported with small changes, and its tests carried over. Some names keep the earlier conventions (`parentId`, `isTodo`, `resources`, Unix milliseconds).

## ADR-7: Snapshot versions per editing session

*Accepted, October 2026.*

**Context.** Autosave writes every few seconds while typing; keeping each save would produce hundreds of versions.

**Decision.** Before a change, store the previous state unless the newest version is younger than `VersionIntervalMinutes` (10). Restoring a version snapshots first.

**Consequences.** One version per editing session, cheap storage, and restores can be undone. Changes within one session can't be told apart.

## ADR-8: Plain Markdown with common extensions, 32-hex ids

*Accepted, October 2026.*

**Context.** Notes must stay plain text that other tools can read, and notes pasted in from other apps should keep their formatting.

**Decision.** GitHub-flavored Markdown plus widely used extensions (KaTeX math, Mermaid, `==mark==`, `++insert++`, `~sub~`, `^sup^`), 32-hex ids, and `:/<id>` links to notes and attachments.

**Consequences.** Pasted Markdown keeps its formatting, and the export is readable anywhere. The renderer carries the extensions as remark plugins.

## ADR-9: One container for API and client

*Accepted, October 2026.*

**Context.** Operation should be as simple as possible.

**Decision.** The backend serves the compiled client from `wwwroot`; one image, one port.

**Consequences.** Same origin for API and UI (no CORS), simple proxy setup. Client and backend are always released together.

## ADR-10: Markdown export for data portability

*Accepted, October 2026.*

**Context.** Users must be able to take their data elsewhere (data ownership goal, CRA Annex I part I (2)(m), GDPR Art. 20).

**Decision.** `GET /api/export` returns a ZIP with one Markdown file per note with YAML front matter, folders per notebook, attachments in `_resources/`, and relative links.

**Consequences.** The export opens in Obsidian, other notes apps that import Markdown with front matter, and any editor. Trash and version history are not part of it.

## ADR-12: Import only the own Markdown export format, into a new notebook

*Accepted, October 2026.*

**Context.** The export (ADR-10) had no way back: moving to another server or restoring one account meant re-creating notes by hand. Importers for other apps' own formats are out of scope.

**Decision.** `POST /api/import` reads the export's format: folders, Markdown/HTML files with the same front matter keys, other files as attachments, relative links. Obsidian vaults and similar folders of Markdown work as a subset. Everything goes into a new top-level notebook, with new ids; nothing existing is merged or overwritten. The archive is read in place, never unpacked to disk, with limits on size, entry count and unpacked size.

**Consequences.** Export and import round-trip notes, notebooks, tags, to-dos, dates, attachments and links. Version history and the trash don't travel, and importing twice creates two copies (deleting the import notebook undoes one). Attachments count against the user's storage like uploads.

## ADR-11: No end-to-end encryption, offline mode or sharing

*Accepted, October 2026.*

**Context.** These were explicitly out of scope; each would shape the whole architecture (client-side crypto and search, sync and conflict handling, permission model).

**Decision.** Not supported. Confidentiality at rest is left to the host (disk encryption).

**Consequences.** A much simpler system. Whoever controls the server can read the notes; this is documented in the security guide.

## ADR-13: Two-factor sign-in with TOTP and recovery codes

*Accepted, October 2026.*

**Context.** A guessed or leaked password gave full access to an account; the only advice was a proxy's forward auth or a VPN. Self-hosters want a second factor that needs no extra service.

**Decision.** Optional time-based one-time passwords (RFC 6238), implemented in a few lines on top of .NET's HMAC instead of a library, with ten one-time recovery codes stored as hashes. The second factor is sent with email and password in the same sign-in request (401 with `details.secondFactor` asks for it), so there is no pending sign-in state. Administrators and the command line can turn it off for a user who lost their phone. Passkeys (WebAuthn) may follow as a second method.

**Consequences.** Works with every authenticator app and needs no new dependency on the server (the client uses a small QR encoder). The TOTP secret must be stored readable; a database copy reveals it, but an attacker also needs the password. It is optional per user, not enforced by the administrator.

## ADR-14: Shared UI package in the repository

*Accepted, October 2026.*

**Context.** Other apps (first *simmer*) should get the same look and the same components without copying them. A package of its own repository or registry adds release overhead before a second consumer exists, and changes to a component would need two pull requests.

**Decision.** The shadcn/ui components, the theme tokens and Tailwind base styles (`theme.css`), the theme provider and toggle, `cn` and `useIsMobile` live in `packages/ui` as the package `@kamineko/ui`. The repository root is an npm workspace with the client and the package and one `package-lock.json`. The package ships TypeScript sources and CSS (no build step); the app's Vite compiles them, and `theme.css` tells Tailwind to scan the components through `@source`. It only depends on React, Radix UI and the styling helpers, never on the app (no `@/` imports, no API or query code).

**Consequences.** Notes changes and its UI change in one pull request, and the compiled app is the same as before. Other apps can't consume the package yet. When they do, it moves without changing its imports:

1. Give the package a build (Vite library mode or tsup) that emits JavaScript, type declarations and the CSS, and point `exports` at the output. Keep `@source` pointing at the built files.
2. Remove `"private": true`, add `publishConfig.registry` (`https://npm.pkg.github.com`) and a workflow that publishes on `ui-v*` tags with `packages: write` only on that job, provenance and an SBOM, like the app release.
3. Consumers add an `.npmrc` with `@kamineko:registry=https://npm.pkg.github.com` (GitHub Packages needs a token for installs, even for public packages) and depend on a version range instead of the workspace.
4. Optionally move `packages/ui` with its history to its own repository (`git subtree split`) once more than one app depends on it.

