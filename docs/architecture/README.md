# Notes architecture documentation

This documentation follows the [arc42](https://arc42.org) template. Each section is a file of its own:

| | Section | Content |
| :--- | :--- | :--- |
| 1 | [Introduction and goals](01-introduction-and-goals.md) | What Notes does, quality goals, stakeholders |
| 2 | [Constraints](02-constraints.md) | Technical, organizational and regulatory constraints |
| 3 | [Context and scope](03-context-and-scope.md) | Users, neighbouring systems and interfaces |
| 4 | [Solution strategy](04-solution-strategy.md) | The fundamental decisions in short |
| 5 | [Building block view](05-building-block-view.md) | Modules of client and backend, data model, REST API |
| 6 | [Runtime view](06-runtime-view.md) | Sign-in, editing and versions, attachments, search, export, trash |
| 7 | [Deployment view](07-deployment-view.md) | Docker Compose in a Proxmox LXC, development environment, CI/CD |
| 8 | [Cross-cutting concepts](08-crosscutting-concepts.md) | Security, isolation, persistence, errors, logging, Markdown dialect, UI, tests |
| 9 | [Architecture decisions](09-architecture-decisions.md) | Decision records |
| 10 | [Quality requirements](10-quality-requirements.md) | Quality tree and scenarios |
| 11 | [Risks and technical debt](11-risks-and-technical-debt.md) | Known risks and open items |
| 12 | [Glossary](12-glossary.md) | Terms |

Related documents:

- [Security guide](../security.md): security model, hardening checklist, security log (for operators)
- [Deployment guide](../deployment.md): installation, reverse proxy, updates, backup and restore
- [CRA compliance](../cra.md): how Notes meets the essential requirements of the EU Cyber Resilience Act
- [SECURITY.md](../../SECURITY.md): reporting vulnerabilities, supported versions

Keep this documentation current: a change to the REST API, the data model, a module's responsibility or a deployment detail updates the matching section in the same pull request.
