# 1. Introduction and goals

## Requirements overview

Notes is a self-hosted notes app for a household or a small team. It runs on a home server, is used in the browser on any device, and keeps all data on that server.

| Feature | Short description |
| :--- | :--- |
| Notebooks | Nested notebooks; create, rename, move, trash and restore. |
| Notes and to-dos | Markdown notes (HTML notes are shown sanitized), to-dos with completion and due dates. |
| Editor | CodeMirror Markdown editor with Edit, Split and Preview modes and autosave. |
| Tags | Tag notes, browse by tag, rename and delete tags. |
| Search | Full-text search over titles and bodies with Joplin's search syntax. |
| Attachments | Upload files and images and show them inline in notes. |
| Version history | Earlier states of a note are kept and can be restored. |
| Trash | Deleted notes and notebooks can be restored or deleted permanently. |
| Users | Several accounts with strict separation; only administrators create accounts. |
| Export | Every user can download all their notes as Markdown files with attachments. |

Notes replaces Joplin Server plus [Joplin Web](https://github.com/KamiNeko/joplin-web) for its maintainer: the browser client of Joplin Web was all that was used, so Notes keeps its interface and Markdown dialect but has its own backend and database.

Explicitly **not** goals: offline use, sharing notes between users or publicly, end-to-end encryption, native apps, and an automatic Joplin importer (notes are copied over by hand).

## Quality goals

| Priority | Quality goal | Motivation |
| :--- | :--- | :--- |
| 1 | **Security and privacy** | Notes hold private data. Strangers on the internet must not get in, users must not see each other's notes, and a stolen backup must not give away passwords or sessions. Security is designed in, not added later (see [section 8](08-crosscutting-concepts.md#security)). |
| 2 | **Data ownership** | Notes are plain Markdown, the database is a standard PostgreSQL, and every user can export everything. Nothing ties the data to Notes. |
| 3 | **Simple, robust operation** | One `docker compose up` on a home server; migrations run by themselves; backup is a database dump plus one volume. |
| 4 | **Usability** | Fast and pleasant in the browser on desktop and phone; the same interface as Joplin Web. |
| 5 | **Maintainability** | A single maintainer must be able to understand and change every part; dependencies are few and current. |

## Stakeholders

| Role | Expectations |
| :--- | :--- |
| Maintainer (Alexander) | Uses Notes daily; develops and operates it. A .NET developer by profession: the backend should feel familiar and idiomatic. |
| Users (household members) | Notes are safe and private, the app is fast and works on the phone. |
| Administrator | Easy installation, updates and backups; account management without a shell. |
| Self-hosters (open-source users) | Clear documentation, secure defaults, verifiable releases. |
| Security researchers | A clear way to report vulnerabilities ([SECURITY.md](../../SECURITY.md)). |
| Contributors and AI assistants | Conventions in `CLAUDE.md`, this documentation, tests that catch regressions. |
