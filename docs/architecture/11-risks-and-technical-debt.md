# 11. Risks and technical debt

| Risk or debt | Impact | Mitigation / plan |
| :--- | :--- | :--- |
| Two-factor sign-in is optional | For users who don't turn it on, a guessed or leaked password gives full access to that account. TOTP secrets are stored readable in the database. | Lockout and rate limits; strong-password minimum; TOTP with recovery codes (ADR-13); the security guide recommends turning it on for internet exposure. Possible improvements: an administrator setting to require it, passkeys (WebAuthn). |
| Administrators can reset passwords | A compromised administrator can take over any account. | Keep few administrators; every reset is logged and ends the victim's sessions (they notice). |
| No encryption at rest by the app | Disk or backup theft exposes notes. | Documented; use host disk encryption and protect backups. |
| Login lock and rate limits are in memory | Reset on restart; don't work across several instances. | Fine for a single container; a shared store would be needed to scale out. |
| One maintainer | Slow fixes when unavailable; bus factor 1. | Automation (CI, Dependabot), documentation, tests; no support period is promised; if the project ends, the repository is archived and the README says so. |
| Database and attachment files can drift | A crash between file and row write leaves an orphan file or a missing one. | Missing files answer 404; with `AttachmentCleanupDays` set, the daily housekeeping deletes files without a row and attachments no note links to. |
| Inherited naming in the API (`parentId`, `resources`, Unix ms) | Slightly unusual for a fresh API. | Kept on purpose (ADR-6); documented. |
| .NET 10 support ends November 2028 | No security fixes afterwards. | Move to .NET 12 (LTS) during 2028. |
| CRA obligations if Notes is ever commercialized | Conformity assessment, CE marking, reporting duties. | [CRA compliance](../cra.md) lists what exists and what would be added. |
