# Security guide

How Notes protects your notes, what it expects from your setup, and how to run it safely. To report a vulnerability, see [SECURITY.md](../SECURITY.md).

## Security model

Notes is built for a household or a small team on a server you control. It assumes:

- **The server is yours.** Whoever controls the host, the Docker daemon or the database can read every note. Notes is not end-to-end encrypted.
- **Accounts are created by an administrator.** There is no public sign-up, so the attack surface for strangers is the sign-in form.
- **Users are separated.** Every user sees only their own notebooks, notes, tags, versions and attachments. Administrators manage accounts but **cannot** read other users' notes through the app.
- **The network may be hostile.** Notes is safe to expose to the internet **behind a reverse proxy with HTTPS**. Plain HTTP is only meant for a trusted home network.

### What it defends against

| Threat | Protection |
| :--- | :--- |
| Password guessing | Per-address rate limit (30 sign-ins per 15 minutes), plus a per-account lock after 10 failed sign-ins in 15 minutes from any address. Unknown accounts are answered exactly like wrong passwords, in the same time. |
| Stolen database or backup | Passwords are stored as PBKDF2-HMAC-SHA512 hashes (210,000 iterations, salted). Sessions are stored as SHA-256 hashes of the token, so a database copy can't be used to sign in. |
| Stolen session cookie | The cookie is `HttpOnly` (not readable by scripts), `SameSite=Strict` and, over HTTPS, `Secure` with the `__Host-` prefix. Sessions end after 30 days without use and at most 90 days after sign-in. Changing your password ends your other sessions; an admin's password reset ends all of them. |
| Cross-site request forgery | `SameSite=Strict`, and every write must come from the app's own `Origin`. |
| Cross-site scripting through note content | Markdown and HTML notes are sanitized before rendering. A strict Content Security Policy (`script-src 'self'`, no inline scripts) blocks injected scripts even if sanitizing ever missed something. |
| Malicious attachments | Attachments are served with `X-Content-Type-Options: nosniff`, a sandboxing CSP, and as downloads unless they are images, PDF, plain text, audio or video. SVG and HTML files are always downloads. File names are cleaned, and files are stored under random ids, never under their uploaded names. |
| Clickjacking, data leaks | `frame-ancestors 'none'`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Cross-Origin-Opener-Policy` and `-Resource-Policy: same-origin`, a restrictive `Permissions-Policy`, and `Cache-Control: no-store` on all API answers. HSTS over HTTPS. |
| Spoofed client addresses | `X-Forwarded-*` headers are ignored unless `Notes__TrustProxy` is on, and even then they are only accepted from the proxy's addresses (`Notes__TrustedProxies`). |
| Malicious import archives | Imports are read in place and never unpacked to disk; entry names only name notebooks and resolve links, absolute paths and `..` are refused. ZIP bombs are refused by entry count (20,000), unpacked size (10 × `Notes__MaxImportMb`) and per-entry size (`Notes__MaxUploadMb`), with every entry read no further than its declared size. Imported files are ordinary attachments with all their protections. |
| Overload | Request bodies are limited (`Notes__MaxUploadMb`, `Notes__MaxImportMb` for imports), passwords longer than 1024 characters are refused before hashing, the API is rate limited per address, and search returns at most 500 results. |
| Misconfiguration | Settings are validated on start: an unknown `Notes__CookieSecure` value, a `Notes__PublicUrl` that isn't an absolute http(s) URL, limits below their minimum or an unparsable `Notes__TrustedProxies` stop the app with a message, instead of running with a weaker policy than intended. |
| Container escape, tampering | The image runs as a non-root user, the compose file runs it with a read-only filesystem, no Linux capabilities and `no-new-privileges`. The database is not published outside the stack. |
| Vulnerable or tampered dependencies | All dependencies are pinned (NuGet and npm lock files, base images by digest, GitHub Actions by commit). Dependabot, `npm audit`, NuGet audit (the build fails on known vulnerabilities), CodeQL and an image scan run in CI. Releases come with SBOMs and signed build provenance. |

### What it does not defend against

- Someone with access to the server, the Docker host, the database or the backups.
- A compromised administrator account: it can reset passwords, so it can take over other accounts. Use a strong, unique password for administrators and keep the number of administrators small.
- A malicious browser extension or a compromised device of a user.
- Note content is not encrypted at rest. Use disk encryption on the host (for example ZFS native encryption or LUKS on the Proxmox storage) if that matters for you.

There is no two-factor authentication yet. If you expose Notes to the internet, put it behind your reverse proxy's authentication (for example Authelia or Authentik forward auth) or a VPN (WireGuard, Tailscale) for a second factor.

## Hardening checklist

Before you open Notes to the internet:

1. **HTTPS through a reverse proxy.** Set `NOTES_PUBLIC_URL=https://notes.example.com` and `NOTES_TRUST_PROXY=true`. If the proxy doesn't run on a private network address, list its address in `NOTES_TRUSTED_PROXIES`. See [deployment.md](deployment.md) for Caddy and Nginx Proxy Manager examples.
2. **Don't publish the port directly.** Bind it to the proxy only, for example `HOST_PORT=127.0.0.1:8080` when the proxy runs on the same host, or use a firewall rule.
3. **Strong secrets.** `POSTGRES_PASSWORD` should be long and random (`openssl rand -hex 24`). Remove `NOTES_ADMIN_PASSWORD` from `.env` after the first start, and keep `.env` readable only by root (`chmod 600 .env`).
4. **Never enable `Notes__SeedDemo`** on a reachable server: it creates an administrator with a published password. Notes logs a warning on every start while the demo account exists.
5. **Keep the hardening in `docker-compose.yml`** (`read_only`, `cap_drop`, `no-new-privileges`).
6. **Update regularly.** Watch the repository's releases (Watch > Custom > Releases and Security alerts) and run the update steps from the README. Verify published images with `gh attestation verify`.
7. **Back up** the database and the attachments volume, and keep the backups as protected as the server (they contain all notes).
8. **Watch the security log** (below), or forward it to your log system.

## Security log

Security events are logged under the category `Notes.Security` with stable event ids. They include the account and the client address, never passwords or session tokens.

| Event id | Event | Level |
| :--- | :--- | :--- |
| 1000 | Sign-in succeeded | Information |
| 1001 | Sign-in failed (wrong password or unknown account) | Warning |
| 1002 | Sign-in refused: account locked after too many failures | Warning |
| 1003 | Signed out | Information |
| 1010 | User changed their own password | Information |
| 1020–1023 | Administrator created a user, reset a password, changed the administrator role, deleted a user | Information |
| 1030 | Write refused: request from a foreign origin (possible CSRF) | Warning |
| 1031 | Rate limit hit | Warning |
| 1040 | The demo account exists | Warning |
| 1050 | User downloaded the export of all their notes | Information |
| 1051 | User imported a ZIP of notes (with the number of notes and attachments) | Information |

Read them with `docker compose logs notes | grep -i "sign-in\|refused\|rate limit\|exported\|imported"`. Docker keeps container logs with the `json-file` driver; set a size limit (`max-size`, `max-file`) in `/etc/docker/daemon.json` and ship them to your log server if you want a longer history. Log lines contain email addresses and IP addresses, which are personal data: keep them only as long as you need them.

## Data protection

- **Deleting data:** notes and notebooks go to the trash first; deleting them from the trash removes them, their versions and tags links from the database. Deleting a user removes all their data, including attachment files. Database backups you made keep the data until you delete them.
- **Version history** keeps earlier states of notes. Set `NOTES_VERSION_RETENTION_DAYS` to delete old versions automatically.
- **No telemetry.** Notes makes no outgoing connections: no analytics, no update checks, no external fonts or scripts. Remote images in notes are blocked unless you set `NOTES_ALLOW_REMOTE_IMAGES=true`.
- **Browser storage:** the client keeps only the theme, a few view settings, the signed-in email and unsaved drafts in `localStorage`. Signing out removes the email and the drafts, and the in-memory note cache and loaded attachments are dropped, so the next person at a shared computer finds nothing.

## Development practices

- Security-relevant code (sessions, accounts, the Origin check, headers, attachment serving) is covered by integration tests in `backend/tests` (`AuthTests`, `SecurityTests`, `ResourceAndUserTests`).
- CI runs on every pull request: build with warnings as errors, tests, NuGet and npm audit, dependency review (new dependencies and their licenses), CodeQL (`security-extended`), an image vulnerability scan, and the OpenSSF Scorecard on `main`.
- Workflows use least-privilege tokens (read-only by default, write permissions only on the jobs that need them) and actions pinned by commit SHA.
