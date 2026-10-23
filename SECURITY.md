# Security policy

Notes stores private notes, so security problems are treated as the most important kind of bug. Thank you for helping keep Notes and its users safe.

## Supported versions

| Version | Security fixes |
| :--- | :--- |
| Latest minor release (`0.x` today) | ✅ Yes |
| Older releases | ❌ No: update to the latest release (migrations run automatically) |

Security fixes are released as a new patch version as soon as they are ready and are announced in a [GitHub Security Advisory](https://github.com/KamiNeko/notes/security/advisories) and the [changelog](CHANGELOG.md).

**No guaranteed support period.** Notes is a free hobby project maintained in spare time. Security fixes are made on a best-effort basis, for the latest release only, for as long as the project is maintained. If it is discontinued, the repository will be archived and the README will say so. See [docs/cra.md](docs/cra.md) for how this relates to the EU Cyber Resilience Act.

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report it privately through GitHub: [**Report a vulnerability**](https://github.com/KamiNeko/notes/security/advisories/new) (Security tab > Advisories > Report a vulnerability). Only the maintainer can see the report.

Please include:

- what an attacker can do, and what they need first (an account? an admin account? network access?)
- the version or commit, and how Notes was deployed (Docker Compose, behind which proxy)
- steps to reproduce, or a proof of concept
- whether you want to be credited, and under which name

Use test data, not real notes, and don't access other people's data beyond what is needed to show the problem.

## What happens next

These are targets, not guarantees: Notes is maintained in spare time, but security reports always come first.

| Step | Target |
| :--- | :--- |
| Acknowledgement of your report | within 3 working days |
| First assessment (confirmed or not, severity using CVSS) | within 10 working days |
| Fix released for critical and high severity issues | within 30 days of confirmation |
| Fix released for medium and low severity issues | in the next regular release, at most 90 days |
| Public advisory (with a CVE ID assigned through GitHub) | when the fix is released |

You get updates in the private advisory as the work progresses, and you can work on the fix together with the maintainer there. We follow **coordinated disclosure**: please give us 90 days, or until a fix is released if that is sooner, before you publish details. If a vulnerability is already being exploited, we release a fix or a workaround as fast as possible and publish an advisory straight away.

If Notes is made available on the EU market in a way that the Cyber Resilience Act applies to, actively exploited vulnerabilities and severe incidents are also notified to the responsible CSIRT and ENISA within the deadlines of Article 14 (an early warning within 24 hours).

## Safe harbor

Research that follows this policy is welcome. We will not take legal action against you for good-faith research that respects users' privacy, does not degrade the service for others and gives us reasonable time to fix the problem before it is disclosed.

## Scope

In scope: the code in this repository, the published container images (`ghcr.io/kamineko/notes`), and the default configuration in `docker-compose.yml`.

Out of scope: problems that need an already compromised server or administrator account, missing hardening in a deployment that ignores the [security guide](docs/security.md) (for example no HTTPS on the internet), denial of service through very high request volumes, and vulnerabilities in third-party dependencies that do not affect Notes (please report those upstream; we still update the dependency if a fix exists).

## Verifying releases

Every published image carries a signed build provenance attestation and an SBOM. Verify an image before you run it:

```bash
gh attestation verify oci://ghcr.io/kamineko/notes:<version> --repo KamiNeko/notes
```

The CycloneDX SBOMs of the image and of the source dependencies are attached to each [GitHub release](https://github.com/KamiNeko/notes/releases).
