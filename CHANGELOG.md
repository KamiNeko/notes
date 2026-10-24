# Changelog

All notable changes to Notes. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org). Security fixes are marked **Security** and also published as [GitHub Security Advisories](https://github.com/KamiNeko/notes/security/advisories).

## [Unreleased]

### Added

- **Two-factor sign-in:** everyone can turn on a second step with an authenticator app (TOTP, with a QR code), with 10 one-time recovery codes (account menu > *Two-factor sign-in*). Administrators can turn it off for a user who lost their phone (*Manage users*, or `users reset-2fa <email>` on the command line).

### Security

- Two-factor sign-in with an authenticator app: a stolen or guessed password alone no longer opens an account that has it on. Wrong codes count towards the account lock, each code works once, and turning it on, off or getting new recovery codes is logged (events 1006, 1007, 1011–1013, 1025).

## [0.2.0] - 2026-10-24

### Added

- **Export:** every user can download all their notes as a ZIP of Markdown files with YAML front matter (compatible with Joplin's Markdown + front matter import), organized in notebook folders, with their attachments and working relative links (account menu > *Export notes*, `GET /api/export`).
- **Security log:** sign-ins, failed sign-ins, account locks, sign-outs, password and account changes, refused cross-origin requests and rate limit hits are logged under `Notes.Security` with stable event ids.
- `Notes__TrustedProxies`: the addresses a reverse proxy may connect from (defaults to private networks).
- Release workflow: multi-arch images on GHCR with signed build provenance, SBOM attestations and CycloneDX SBOMs attached to each GitHub release.
- CodeQL, OpenSSF Scorecard, dependency review, NuGet and npm audit and a container image scan in CI; Dependabot for all dependency ecosystems.
- [Contributing guide](CONTRIBUTING.md) and issue and pull request templates.
- Documentation: [security policy](SECURITY.md), [security guide](docs/security.md), [deployment guide](docs/deployment.md), [CRA compliance](docs/cra.md) and the architecture documentation in [arc42](docs/architecture/README.md) form.

### Changed

- **Breaking:** passwords need at least **12 characters** (was 8) and at most 1024. Existing passwords keep working; the rule applies the next time a password is set.
- Passwords are hashed with 210,000 PBKDF2 iterations (was 100,000). Existing hashes are upgraded on the next sign-in.
- Sessions end at the latest 90 days after sign-in, even when in use (`Notes__SessionMaxAgeDays`).
- **Breaking:** invalid settings stop the start with a message naming them (an unknown `Notes__CookieSecure` value, a `Notes__PublicUrl` that isn't an absolute http(s) URL, limits below their minimum, unparsable `Notes__TrustedProxies`). Before, such values were silently replaced by defaults.
- `docker-compose.yml` runs the app with a read-only filesystem, without Linux capabilities and with `no-new-privileges`. Base images are pinned by digest, GitHub Actions by commit, NuGet packages by lock file.

### Security

- An account is locked for 15 minutes after 10 failed sign-ins, from any address (`Notes__LoginMaxFailures`).
- Forwarded headers are only accepted from trusted proxy addresses.
- New response headers: `Strict-Transport-Security` over HTTPS, `Permissions-Policy`, `Cache-Control: no-store` for API responses; the `Server` header is no longer sent.
- Overlong passwords are refused before hashing.
- A password change or reset that fails to save no longer ends the user's sessions while keeping the old password: the new hash and the session cleanup are one transaction, and the security log entry is written only after it committed.
- Exports are logged as security event 1050 (who, from which address), since a full export is the first thing to look for after a stolen session.

## [0.1.0] - 2026-10-23

First release.

### Added

- Notebooks (nested), notes and to-dos with due dates, tags, trash, version history with restore, full-text search with filters, image and file attachments, conflict detection.
- Markdown editor with Edit, Split and Preview modes and Joplin's Markdown extensions (math, Mermaid, highlight, insert, sub- and superscript); sanitized HTML notes.
- Accounts created by an administrator, user management in the app and on the command line, per-user data isolation.
- Server-side sessions with `HttpOnly`, `SameSite=Strict` cookies, Origin check, Content Security Policy, rate limits.
- Docker image, Docker Compose stack with PostgreSQL 17, dev container, CI.

[Unreleased]: https://github.com/KamiNeko/notes/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/KamiNeko/notes/releases/tag/v0.2.0
[0.1.0]: https://github.com/KamiNeko/notes/releases/tag/v0.1.0
