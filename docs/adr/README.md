# Architecture Decision Records

Lightweight ADRs ([Nygard format](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions)).
One decision per file, never edited after acceptance — superseded by a new ADR instead.

| ADR | Decision | Status |
|---|---|---|
| [0001](0001-modular-monolith.md) | Modular monolith, single deployable backend | Accepted |
| [0002](0002-backend-dotnet-postgresql.md) | Backend: ASP.NET Core (.NET LTS) + PostgreSQL + EF Core | Accepted |
| [0003](0003-frontend-angular.md) | Frontend: Angular SPA | Superseded by 0011 |
| [0004](0004-authentication.md) | Authentication: ASP.NET Core Identity, cookies, mandatory TOTP 2FA | Accepted |
| [0005](0005-file-storage-and-photos.md) | Photos in S3-compatible private object storage | Accepted |
| [0006](0006-containers-and-environments.md) | Docker for development and deployment | Accepted |
| [0007](0007-i18n.md) | Runtime i18n with react-i18next: es, ca-ES-valencia, en | Accepted |
| [0008](0008-documents-and-exports.md) | Excel and PDF generation on the server | Accepted |
| [0009](0009-design-system-shadcn-tailwind.md) | Design system: shadcn/ui + Tailwind v4 with project guardrails | Accepted (§5 superseded by 0012) |
| [0010](0010-open-source-license.md) | Open-source license: MIT | Accepted |
| [0011](0011-frontend-react.md) | Frontend: React + Vite SPA, fixed toolset, PWA | Accepted |
| [0012](0012-polvorapp-visual-identity.md) | PolvorApp visual identity (own brand, no third-party assets in git) | Superseded by 0013 |
| [0013](0013-polvora-visual-identity.md) | "Pólvora" visual identity: own fonts and palette, night sidebar, ember accent | Accepted |
| [0014](0014-charts.md) | Charts with Recharts through shadcn/ui | Accepted |

## Template

```markdown
# NNNN. Title

- Status: Proposed | Accepted | Superseded by NNNN
- Date: YYYY-MM-DD

## Context
## Decision
## Consequences
## Alternatives considered
```
