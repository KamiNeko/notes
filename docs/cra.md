# CRA compliance

How Notes meets the requirements of the EU **Cyber Resilience Act** (Regulation (EU) 2024/2847, "CRA"), and what is still open. The CRA applies in stages: the reporting duties of Article 14 apply from 11 September 2026, all other obligations from 11 December 2027.

> [!NOTE]
> This page is the project's own assessment, written to make the security work traceable. It is not legal advice and not an EU declaration of conformity.

## Does the CRA apply to Notes?

The CRA covers "products with digital elements" that are **made available on the EU market in the course of a commercial activity**. Free and open-source software that is developed and published without monetization (no price, no paid support tied to the product, no use of personal data for profit) is outside its scope (recitals 18 and 19). Notes is a free hobby project, published that way: no price, no paid support, no data collection. **The CRA does not apply to it**, and none of its obligations (such as a support period, CE marking or reporting duties) are binding.

Notes follows the CRA's practices **voluntarily**, for two reasons: its requirements are a good, concrete definition of "secure software", and a product that already meets them can be offered commercially later without a rewrite.

If Notes is ever made available commercially (sold, offered with paid support, or bundled into a paid product), the following would be added:

| Obligation | Status today |
| :--- | :--- |
| Risk assessment (Art. 13(2)) | Exists as the threat table in the [security guide](security.md) and the risks in [arc42 section 11](architecture/11-risks-and-technical-debt.md). Would be turned into a formal, dated document. |
| Classification | Notes is not listed in Annex III or IV (not an "important" or "critical" product), so it falls into the default category. |
| Conformity assessment | Default category: internal control (module A, Annex VIII), done by the manufacturer itself. |
| Technical documentation (Annex VII) | Mostly exists: architecture ([arc42](architecture/README.md)), security guide, this page, SBOMs, test results in CI. Would be collected per release and kept for 10 years. |
| EU declaration of conformity (Annex V) and CE marking | Not issued. Would be added to each release. |
| Manufacturer name and postal address (Annex II) | Not published. Would be added to the README and releases. |
| Support period of at least 5 years (Art. 13(8)) | None declared: security fixes are best effort, for the latest release only ([SECURITY.md](../SECURITY.md#supported-versions)). A support period would be declared. |
| Reporting to ENISA's single reporting platform (Art. 14) | The process is described in [SECURITY.md](../SECURITY.md). |

## Essential requirements: product properties (Annex I, Part I)

**(1) Appropriate level of cybersecurity based on the risks.** Notes stores private notes and is meant to be reachable over the internet behind a proxy. The threats and the protections against each are listed in the [security guide](security.md#what-it-defends-against); the quality scenarios in [arc42 section 10](architecture/10-quality-requirements.md) make them testable.

| Requirement | How Notes meets it | Gaps |
| :--- | :--- | :--- |
| **(a)** No known exploitable vulnerabilities when released | CI fails on known vulnerabilities in NuGet packages (NuGet audit, `dotnet list package --vulnerable`), npm packages (`npm audit`), and in the container image (Grype, high and critical with a fix). CodeQL scans the code. Releases are only built after all checks pass. | |
| **(b)** Secure by default configuration, reset to the original state | No public sign-up; HTTPS-only cookie (`Secure`, `__Host-`) as soon as the public URL is `https://`; forwarded headers ignored unless a proxy is configured; remote images blocked; demo account off; the compose file runs the container read-only, without capabilities and as a non-root user; the database isn't published. Reset: `docker compose down -v` removes all data and settings. | |
| **(c)** Vulnerabilities can be fixed through security updates | Security fixes are released as patch versions and announced in GitHub Security Advisories. Updating is `git pull` or a new image tag, then `docker compose up -d`; migrations run automatically. | No **automatic** updates: as a self-hosted server, the administrator decides when to update. Watching the repository's releases notifies them. |
| **(d)** Protection from unauthorised access, reporting of possible unauthorised access | Accounts only by an administrator; PBKDF2-HMAC-SHA512 (210,000 iterations); passwords of 12 to 1024 characters (NIST SP 800-63B); per-address rate limits and per-account lock; server-side sessions stored as hashes, ending after 30 days idle and 90 days at most, which users can review and end themselves; strict per-user data isolation in every query; Origin check against CSRF. Failed sign-ins, locks and refused requests are written to the security log. | No built-in two-factor authentication yet; the security guide recommends forward auth or a VPN for internet exposure. |
| **(e)** Confidentiality of stored and transmitted data | In transit: HTTPS through the reverse proxy, HSTS, `Secure` cookies; the database is only reachable inside the compose network. Stored: passwords and session tokens are hashed; attachments are stored under random ids; responses are `Cache-Control: no-store`. | Notes and attachments are **not encrypted at rest by the app**. The deployment guide recommends encrypted storage on the host (ZFS native encryption, LUKS). |
| **(f)** Integrity of data, commands, programs and configuration | Every write is authenticated, checked for the right owner and the request origin; conflicting edits are detected (409) instead of overwriting; earlier versions of notes are kept. The program: dependencies pinned by lock file and digest, actions by commit SHA, read-only container filesystem, release images with signed build provenance (`gh attestation verify`). | |
| **(g)** Data minimisation | Notes stores accounts (email, optional display name, password hash), sessions (with a shortened User-Agent to label them, no address), and the user's own content. No telemetry, no analytics, no tracking, no external requests. The browser keeps only view settings, the signed-in email and unsaved drafts, and removes the last two on sign-out. | |
| **(h)** Availability of essential functions, resilience against denial of service | Rate limits per address, request size limits, password length limit before hashing, search capped at 500 results, export written to disk instead of memory, a health check in the image and automatic restarts in compose. | Large-scale network attacks have to be handled in front of Notes (proxy, firewall). |
| **(i)** Minimal impact on other devices and networks | Notes makes no outgoing connections at all. It doesn't send mail, fetch URLs or load remote content (unless remote images are turned on, and then only in the user's browser). | |
| **(j)** Limited attack surface | One published port; database internal only; no public registration; no plugin system; no server-side rendering of user content; diagnostics (`DOTNET_EnableDiagnostics=0`) and the `Server` header off; minimal ASP.NET runtime image. | |
| **(k)** Exploitation mitigation | Strict Content Security Policy (`script-src 'self'`) on top of sanitizing; sandboxing CSP and forced downloads for risky attachment types; `nosniff`, `frame-ancestors 'none'`, COOP/CORP; non-root, read-only container without capabilities and with `no-new-privileges`. | |
| **(l)** Security-relevant logging and monitoring, with an opt-out | Security events (sign-ins, failures, locks, sign-outs, password and account changes, refused requests, rate limits, demo account, exports) are logged under `Notes.Security` with stable event ids, never with passwords or tokens. See [security log](security.md#security-log). Opt-out: set `Logging__LogLevel__Notes.Security=None`. | |
| **(m)** Users can remove all data permanently and move it to other systems | Deleting from the trash removes notes and their versions; deleting a user removes all their data including attachment files. **Export:** every user can download all their notes as a ZIP of Markdown files with YAML front matter (Joplin-compatible) and their attachments (account menu > *Export notes*). Decommissioning is described in the [deployment guide](deployment.md#removing-notes). | Backups made by the administrator keep data until they are deleted. |

## Vulnerability handling (Annex I, Part II)

| Requirement | How Notes meets it |
| :--- | :--- |
| **(1)** Identify and document components, SBOM | Every release has CycloneDX SBOMs of the container image and of the source dependencies attached, plus a BuildKit SBOM attestation on the image. Dependency versions are fixed by lock files. |
| **(2)** Remediate without delay, security updates separate from features | Dependabot proposes updates weekly and immediately for security alerts. Security fixes are released as their own patch versions. Targets: critical and high within 30 days, others within 90 days ([SECURITY.md](../SECURITY.md)). |
| **(3)** Regular security tests and reviews | Integration tests for sessions, accounts, isolation, the Origin check, headers and attachments run on every change; CodeQL (`security-extended`), dependency review, NuGet and npm audit and an image scan run in CI; the OpenSSF Scorecard checks the project's practices weekly. |
| **(4)** Public disclosure of fixed vulnerabilities | Each fix gets a GitHub Security Advisory with description, affected versions, impact, CVSS severity and remediation, and a CVE ID, plus an entry in the [changelog](../CHANGELOG.md). |
| **(5)** Coordinated vulnerability disclosure policy | [SECURITY.md](../SECURITY.md): private reporting, response targets, 90-day coordinated disclosure, safe harbor. |
| **(6)** Contact address for reporting vulnerabilities | GitHub private vulnerability reporting: [Report a vulnerability](https://github.com/KamiNeko/notes/security/advisories/new). |
| **(7)** Secure distribution of updates | Updates come from the GitHub repository over HTTPS or as images from GHCR with signed build provenance and SBOM attestations that users can verify before they run them. |
| **(8)** Updates free of charge, with advisories | All releases are free. Advisories explain what to do. |

## Information for users (Annex II)

| Item | Where |
| :--- | :--- |
| Manufacturer and contact | Alexander Giesler, via [GitHub](https://github.com/KamiNeko) (see the [license](../LICENSE)). |
| Single point of contact for vulnerabilities | [SECURITY.md](../SECURITY.md#reporting-a-vulnerability) |
| Product name and type | Notes, a self-hosted web application for Markdown notes ([README](../README.md)). |
| Intended purpose, essential functions, security environment | [README](../README.md), [security model](security.md#security-model), [arc42 sections 1 and 3](architecture/README.md) |
| Known circumstances that lead to risks | [What it does not defend against](security.md#what-it-does-not-defend-against), [hardening checklist](security.md#hardening-checklist), [risks](architecture/11-risks-and-technical-debt.md) |
| Type of support and end of the support period | Best-effort security fixes for the latest release, no guaranteed period ([SECURITY.md](../SECURITY.md#supported-versions)) |
| Secure setup and commissioning | [Deployment guide](deployment.md), [hardening checklist](security.md#hardening-checklist) |
| How changes to the setup affect security | [Configuration](../README.md#configuration) and the [hardening checklist](security.md#hardening-checklist) |
| Installing security updates | [Updating](deployment.md#updating) |
| Secure decommissioning and data removal | [Removing Notes](deployment.md#removing-notes) |
| SBOM | Attached to each [release](https://github.com/KamiNeko/notes/releases) |

## Keeping this page true

This page describes the current code. When a change touches authentication, sessions, data storage, logging, dependencies, the build or the release process, check the matching row here and in the [security guide](security.md) in the same pull request.
