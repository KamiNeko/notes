<div align="center">

# Notes

**Your Markdown notes, self-hosted, in any browser.**

A fast, modern notes app for your home server: notebooks, tags, search, attachments, to-dos and version history.<br>
One Docker Compose stack, your own PostgreSQL, no third-party services.

[![CI](https://github.com/KamiNeko/notes/actions/workflows/ci.yml/badge.svg)](https://github.com/KamiNeko/notes/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
<br>
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org)
[![React](https://img.shields.io/badge/React-087EA4?logo=react&logoColor=white)](https://react.dev)
[![Vite](https://img.shields.io/badge/Vite-646CFF?logo=vite&logoColor=white)](https://vite.dev)
[![Tested with Playwright](https://img.shields.io/badge/tested%20with-Playwright-2EAD33?logo=playwright&logoColor=white)](client/e2e)

![Notes showing a note with a table and task list in dark mode](docs/images/notes-dark.png)

</div>

## Why Notes?

Notes started as [Joplin Web](https://github.com/KamiNeko/joplin-web), a browser client for Joplin Server. It turned out the browser was all that was needed, so Notes keeps the same interface and drops Joplin from the setup: a .NET backend stores everything in PostgreSQL, next to your other self-hosted services, but fully separate from them.

- 🏠 **Self-hosted, all the way.** The app and its database run in one Docker Compose stack, for example in a Proxmox LXC.
- 📝 **Plain Markdown.** Notes are Markdown, with the same extensions as Joplin, so notes copied over from Joplin render the same.
- 👥 **Accounts for the household.** An administrator adds users; there is no public registration. Every user sees only their own notes.
- 🕰️ **Version history.** Earlier states of a note are kept, so you can look at them and bring them back.

Not included, on purpose: offline mode, sharing notes with others, and end-to-end encryption.

## Features

| | |
| :--- | :--- |
| 📚 **Notebooks** | Nested notebooks; create, rename, move (into another notebook or to the top level) and delete them. |
| 📝 **Markdown editor** | CodeMirror editor with *Edit*, *Split* and *Preview* modes, autosave and `Ctrl+S`. Formatting toolbar (also on phones) and shortcuts: `Ctrl+B` bold, `Ctrl+I` italic, `Ctrl+E` code, `Ctrl+K` link. Links to other notes via a search picker or *Copy Markdown link*. |
| ✨ **Rich rendering** | GitHub-flavored Markdown: tables, task lists, syntax-highlighted code blocks, plus math (KaTeX), Mermaid diagrams, `==highlight==`, `++insert++`, `~sub~` and `^sup^`. HTML notes are shown as sanitized HTML. |
| 🖼️ **Images & attachments** | Drop, paste or attach files; images are shown inline. |
| ✅ **To-dos** | Create to-dos and tick them off, right from the note list. Set, change or clear due dates; overdue to-dos are highlighted. |
| 🏷️ **Tags** | Tag notes, browse by tag, rename and delete tags. |
| 🔎 **Search** | Instant search across titles and content (`Ctrl+K`): all words must match, `"exact phrases"`, `-word` to exclude, and filters `tag:`, `notebook:`, `type:todo`, `iscompleted:0`. |
| 🕰️ **Version history** | Before an editing session changes a note, its previous state is kept as a version. Preview any version and restore it; the replaced text becomes a version too, so a restore can be undone. Optional retention period. |
| 🗑️ **Trash** | Deleted notes and notebooks go to the trash, where you can restore them or delete them permanently. |
| ⚔️ **Conflict detection** | If a note was changed in another tab or on another device while you edited it, you choose which version to keep. |
| 👥 **Users** | Administrators add accounts, reset passwords, grant the administrator role and delete users (account menu > *Manage users*). Everyone can change their own password. |
| 🛟 **No lost typing** | Unsaved text is kept on the device while offline or after the session expires, saves retry when the connection returns, and you can restore a draft later. |
| 🌗 **Light & dark mode** | Switch themes with one click. |
| 📱 **Responsive** | Works on phones and tablets as well as on the desktop. |

## Screenshots

<table>
  <tr>
    <td colspan="2"><img src="docs/images/notes-light.png" alt="Light mode with an embedded image"></td>
  </tr>
  <tr>
    <td colspan="2" align="center"><em>Light mode, with an image attachment</em></td>
  </tr>
  <tr>
    <td colspan="2"><img src="docs/images/version-history.png" alt="Version history of a note"></td>
  </tr>
  <tr>
    <td colspan="2" align="center"><em>Version history: preview an earlier state and restore it</em></td>
  </tr>
  <tr>
    <td colspan="2"><img src="docs/images/editor-split.png" alt="Split-view Markdown editor"></td>
  </tr>
  <tr>
    <td colspan="2" align="center"><em>Split-view editor with live preview</em></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/images/mobile-list.png" alt="Note list on a phone" width="300"></td>
    <td align="center"><img src="docs/images/mobile-note.png" alt="Note on a phone" width="300"></td>
  </tr>
  <tr>
    <td colspan="2" align="center"><em>On a phone</em></td>
  </tr>
</table>

## Getting started

You need **Docker** with Docker Compose. In a Proxmox LXC, enable *nesting* for the container (Options > Features) and install Docker inside it.

```bash
git clone https://github.com/KamiNeko/notes.git
cd notes
cp .env.example .env
# Edit .env: POSTGRES_PASSWORD, NOTES_ADMIN_EMAIL and NOTES_ADMIN_PASSWORD
docker compose up -d --build
```

Open `http://<host>:8080` and sign in with the administrator account from `.env`. The account is created on the first start; afterwards you can remove the two `NOTES_ADMIN_*` lines. Add further users in the app (account menu > *Manage users*).

The stack has two containers: `notes` (the app, port 8080) and `notes-db` (PostgreSQL 17, not published). Attachments live in the `notes-files` volume, everything else in the database (`notes-db` volume).

> [!IMPORTANT]
> Put Notes behind a reverse proxy with **HTTPS** (Caddy, Traefik, Nginx Proxy Manager, …) before you use it outside your home network. Then also set `NOTES_PUBLIC_URL` and `NOTES_TRUST_PROXY=true`.

### Updating

```bash
git pull
docker compose up -d --build
```

Database migrations run automatically on start.

### Configuration

Settings go into `.env`; `docker-compose.yml` passes them to the app. Every app setting can also be set directly as `Notes__<Name>` (ASP.NET Core configuration).

| `.env` | App setting | Default | Meaning |
| :--- | :--- | :--- | :--- |
| `POSTGRES_PASSWORD` | | (required) | Password of the database user `notes`. |
| `NOTES_ADMIN_EMAIL`, `NOTES_ADMIN_PASSWORD` | `Notes__Admin__Email`, `Notes__Admin__Password` | | First administrator, created while there are no users yet. |
| `HOST_PORT` | | `8080` | Host port the app is published on. |
| `NOTES_PUBLIC_URL` | `Notes__PublicUrl` | | The URL you open, e.g. `https://notes.example.com`. Allowed as request origin; with `https://` the session cookie is `Secure`. |
| `NOTES_TRUST_PROXY` | `Notes__TrustProxy` | `false` | Trust `X-Forwarded-For/-Proto/-Host` from a reverse proxy. |
| `NOTES_VERSION_RETENTION_DAYS` | `Notes__VersionRetentionDays` | `0` | Delete versions older than this many days (`0` keeps all). |
| `NOTES_MAX_UPLOAD_MB` | `Notes__MaxUploadMb` | `25` | Largest attachment. |
| `NOTES_ALLOW_REMOTE_IMAGES` | `Notes__AllowRemoteImages` | `false` | Show `https:` images from other websites in notes. |
| | `Notes__VersionIntervalMinutes` | `10` | Edits within this many minutes of the last version don't create another one. |
| | `Notes__SessionLifetimeDays` | `30` | A session ends after this many days without use. |
| | `Notes__CookieSecure` | `auto` | `auto` (Secure over HTTPS), `true` or `false`. |
| | `Notes__RateLimitMax`, `Notes__LoginRateLimitMax` | `1000`, `30` | API requests and login attempts per client address and 15 minutes. |

### Managing users from the command line

Useful if you lock yourself out:

```bash
docker compose exec notes dotnet Notes.Api.dll users list
docker compose exec -it notes dotnet Notes.Api.dll users add alice@example.com --name "Alice" [--admin]
docker compose exec -it notes dotnet Notes.Api.dll users set-password alice@example.com
```

The password is read from standard input (or from `NOTES_PASSWORD`).

### Backup

Back up the database and the attachments:

```bash
docker compose exec -T db pg_dump -U notes -Fc notes > notes-$(date +%F).dump
docker run --rm -v notes_notes-files:/data -v "$PWD":/backup alpine tar czf /backup/notes-files-$(date +%F).tar.gz -C /data .
```

Restore with `pg_restore -U notes -d notes --clean` (inside the `db` container) and by extracting the archive into the volume. A Proxmox backup of the whole LXC works as well.

## Copying notes over from Joplin

Notes uses the same Markdown dialect as Joplin, including links to other notes and attachments written as `:/<32-character id>`. Copying a note's Markdown from Joplin into a new note keeps its formatting. Links to other notes only work once those notes exist here with their ids, and attachments need to be added again. There is no automatic importer.

## Development

Everything needed (the .NET 10 SDK, Node.js 24, PostgreSQL 17) comes with the **dev container**: open the folder in VS Code (from WSL too) and choose *Reopen in Container*. Without it, install those three yourself and create a database user `notes` with password `notes`.

```bash
# Backend: API on http://localhost:5080, Development settings seed a demo account
dotnet run --project backend/src/Notes.Api

# Client: http://localhost:5173, proxies /api to the backend
npm --prefix client run dev
```

Demo login: `demo@notes.test` / `demo-password`.

| Command | |
| :--- | :--- |
| `cd backend && dotnet test` | Backend integration tests (each test class gets its own database, `NOTES_TEST_DATABASE`) |
| `npm --prefix client run lint` / `test` / `build` | Client lint, unit tests (Vitest), production build |
| `npm --prefix client run test:e2e` | Playwright smoke tests; starts backend and client itself (`E2E_DATABASE`) |
| `npm --prefix client run screenshots` | README screenshots into `docs/images` (backend and client must run) |

How the pieces fit together: [docs/architecture.md](docs/architecture.md).

## License

[MIT](LICENSE)
