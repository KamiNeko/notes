# 3. Context and scope

## Business context

```mermaid
flowchart LR
    U["👤 User<br/>reads and writes notes"]
    A["👤 Administrator<br/>manages accounts, operates the server"]
    N(("Notes"))
    B[("Backups<br/>database dump + attachments")]
    O["Other apps<br/>Obsidian, Joplin, editors"]
    J["Joplin<br/>(previous system)"]
    U -->|"browser: notes, notebooks, tags, search, attachments, history"| N
    A -->|"browser: user administration<br/>shell: users CLI, docker compose"| N
    N -->|"Markdown export (ZIP)"| O
    J -.->|"Markdown copied over by hand"| N
    N -->|"pg_dump, volume archive"| B
```

| Partner | Input to Notes | Output from Notes |
| :--- | :--- | :--- |
| User | Credentials, notes, attachments, search queries | Rendered notes, lists, search results, attachments, the export ZIP |
| Administrator | New accounts, password resets, roles; configuration in `.env` | Account list, security log |
| Joplin | Markdown text pasted into new notes | – |
| Other apps | – | Export: Markdown with YAML front matter, attachments, relative links |
| Backup system | – | Database dump and attachment archive |

## Technical context

```mermaid
flowchart LR
    Browser["Browser<br/>(React SPA)"]
    Proxy["Reverse proxy<br/>TLS termination<br/>(Caddy, Nginx Proxy Manager, ...)"]
    subgraph Stack["Docker Compose stack"]
        App["notes container<br/>Notes.Api + client bundle<br/>:8080"]
        DB[("notes-db container<br/>PostgreSQL 17<br/>:5432, internal only")]
        Files[/"notes-files volume<br/>/data/attachments"/]
    end
    Browser -->|"HTTPS"| Proxy
    Proxy -->|"HTTP + X-Forwarded-*"| App
    Browser -.->|"HTTP (home network only)"| App
    App -->|"Npgsql"| DB
    App --> Files
```

| Interface | Technology | Notes |
| :--- | :--- | :--- |
| Web UI and REST API | HTTP on port 8080, JSON under `/api`, static files otherwise | Session cookie (browser) or `Authorization: Bearer` (API clients). See [section 5](05-building-block-view.md#rest-api). |
| Reverse proxy | `X-Forwarded-For`, `-Proto`, `-Host` | Only honoured with `Notes__TrustProxy` and only from `Notes__TrustedProxies`. |
| Database | PostgreSQL protocol (Npgsql), connection string `ConnectionStrings__Notes` | Not published outside the Compose network. |
| File storage | Local filesystem, `Notes__DataDirectory` (`/data` in the container) | Attachments and temporary export files. |
| Command line | `dotnet Notes.Api.dll users …`, `… healthcheck` | Run inside the container with `docker compose exec`. |
| Logs | Console (stdout), read by Docker | Security events under the category `Notes.Security`. |

There are no outgoing connections: no telemetry, no update checks, no external services.
