# 2. Constraints

## Technical constraints

| Constraint | Background |
| :--- | :--- |
| Backend in **.NET 10** (C#, ASP.NET Core) | The maintainer's professional stack; long-term support release. |
| **PostgreSQL** as the only database | Robust, well known, good full-text tooling (`pg_trgm`), easy to back up. |
| Client with **React, Vite, Tailwind CSS, shadcn/ui** | Same stack and same look as Joplin Web, whose client is reused. |
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
| UI shared with Joplin Web (and later other projects) | UI code must stay extractable into a shared library: only `client/src/lib/api.ts` knows the backend. |

## Conventions

| Convention | Background |
| :--- | :--- |
| Joplin's Markdown dialect, 32-hex ids and `:/<id>` links | Notes copied over from Joplin render the same, and links keep their form. |
| REST API shape of Joplin Web | The client could be reused without rewriting its data layer. |
| Times as Unix milliseconds in the API | Same as Joplin Web. |

## Regulatory constraints

| Constraint | Consequence |
| :--- | :--- |
| EU **Cyber Resilience Act** (Regulation (EU) 2024/2847) | Secure by design and by default, vulnerability handling, SBOMs, security updates for a defined support period, documentation for users. See [CRA compliance](../cra.md). |
| **GDPR** | Notes and logs contain personal data. Data minimisation, deletion and export; no tracking. The operator of an installation is the controller. |
