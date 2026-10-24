# 2. Constraints

## Technical constraints

| Constraint | Background |
| :--- | :--- |
| Backend in **.NET 10** (C#, ASP.NET Core) | The maintainer's professional stack; long-term support release. |
| **PostgreSQL** as the only database | Robust, well known, good full-text tooling (`pg_trgm`), easy to back up. |
| Client with **React, Vite, Tailwind CSS, shadcn/ui** | The stack of the reused client; a shared UI library with other projects is planned. |
| **Docker Compose** deployment in a **Proxmox LXC** | The maintainer's home server setup; fully standalone (no shared database or services with other apps). |
| **Dev container** | Development on Windows with WSL must work with all dependencies (SDK, Node, Postgres) coming from the container. |
| Runs on amd64 and arm64 | Home servers and single-board computers. |
| No outgoing connections | No telemetry, CDNs or external fonts: the app works in an isolated network. |

## Organizational constraints

| Constraint | Background |
| :--- | :--- |
| One maintainer | Simplicity beats features; automation (CI, Dependabot) carries routine work. |
| Open source under the **MIT license** on GitHub (`KamiNeko/notes`) | Public code, issues, security advisories and releases. |
| Conventional commits, pull requests against `main` | See `CLAUDE.md`. |
| UI to be shared with other projects later | UI code must stay extractable into a shared library: only `client/src/lib/api.ts` knows the backend. |

## Conventions

| Convention | Background |
| :--- | :--- |
| GitHub-flavored Markdown with common extensions, 32-hex ids and `:/<id>` links | Markdown pasted in from other apps renders the same; ids and links have one fixed form. |
| REST API shape of the reused client | The client could be reused without rewriting its data layer. |
| Times as Unix milliseconds in the API | Same as the reused client. |

## Regulatory constraints

| Constraint | Consequence |
| :--- | :--- |
| EU **Cyber Resilience Act** (Regulation (EU) 2024/2847) | Not binding (free, non-commercial project), followed voluntarily: secure by design and by default, vulnerability handling, SBOMs, documentation for users. No support period is declared. See [CRA compliance](../cra.md). |
| **GDPR** | Notes and logs contain personal data. Data minimisation, deletion and export; no tracking. The operator of an installation is the controller. |
