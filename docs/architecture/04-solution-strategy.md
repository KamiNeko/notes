# 4. Solution strategy

| Goal or constraint | Approach | Details |
| :--- | :--- | :--- |
| Same UI as Joplin Web, little rewriting | Keep the client and its REST API shape; replace only the backend behind it. | [ADR-6](09-architecture-decisions.md#adr-6-keep-joplin-webs-rest-api) |
| Independence from Joplin | Own backend with its own data model; Joplin's Markdown dialect and id format stay. | [ADR-1](09-architecture-decisions.md#adr-1-own-backend-instead-of-joplin-server), [ADR-8](09-architecture-decisions.md#adr-8-joplin-compatible-markdown-and-ids) |
| Familiar, maintainable backend | ASP.NET Core minimal APIs, thin endpoints, one service per resource, EF Core. | [ADR-2](09-architecture-decisions.md#adr-2-net-10-minimal-apis-and-ef-core) |
| Search without extra infrastructure | PostgreSQL with trigram indexes; ranking and snippets in C#. | [ADR-3](09-architecture-decisions.md#adr-3-search-in-postgresql) |
| Simple backups, small database | Attachments as files on a volume, metadata in the database. | [ADR-4](09-architecture-decisions.md#adr-4-attachments-on-the-filesystem) |
| Security first | Server-side sessions with hashed tokens, per-user scoping in every query, strict CSP, defence in depth in the container, automated supply-chain checks. | [ADR-5](09-architecture-decisions.md#adr-5-server-side-sessions-instead-of-jwt), [section 8](08-crosscutting-concepts.md#security) |
| Never lose text | Autosave with conflict detection, drafts in the browser, version history, trash. | [ADR-7](09-architecture-decisions.md#adr-7-snapshot-versions-per-editing-session) |
| One-command operation | One container serves API and client; migrations on start; Compose stack with its own Postgres. | [ADR-9](09-architecture-decisions.md#adr-9-one-container-for-api-and-client) |
| Data ownership | Plain Markdown, standard PostgreSQL, Markdown export for every user. | [ADR-10](09-architecture-decisions.md#adr-10-markdown-export-for-data-portability) |
