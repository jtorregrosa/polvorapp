# Proposal

## Why

PolvorApp has finished discovery (docs v1.0, ADR-0001..0011) but has no code. Every later change in
`docs/mvp.md` needs the same foundation — solution layout, containers, CI quality gates, i18n
scaffolding, the generated API client and an empty UI shell — so it must exist, tested and green,
before the design system (#2) and the first feature (#3) are built.

Capability (from `docs/mvp.md`): **`platform`** — change #1 of the sequence.

## What Changes

- **Backend skeleton** (ADR-0001, ADR-0002): ASP.NET Core on the current .NET LTS, Minimal APIs,
  modular-monolith layout with a module contract and architecture tests guarding module boundaries,
  central package management, nullable + warnings-as-errors, OpenAPI document committed to the repo.
- **Platform endpoints**: liveness/readiness health checks (PostgreSQL) and a system-info endpoint
  (application version), anonymous and free of personal data.
- **Cross-cutting API behaviour**: RFC 9457 problem details without internal details outside
  Development, structured logs with correlation id and no personal data (NFR-12), request culture
  resolution for `es-ES`, `ca-ES-valencia`, `en` (ADR-0007), security response headers,
  fail-fast configuration validation from environment variables (NFR-14).
- **Synthetic seed runner** (ADR-0002, SEC-11): deterministic seeding entry point that refuses to run
  in Production; no domain seeders yet (they arrive with each module).
- **Frontend skeleton** (ADR-0011): React + TypeScript strict + Vite, React Router, TanStack Query,
  orval-generated client from the committed OpenAPI document, feature-folder layout
  (`src/features/<capability>/`, `src/components/{ui,app}/`), ESLint (typescript-eslint, jsx-a11y,
  no literal strings) + Prettier, Vitest + Testing Library + axe, installable PWA manifest.
- **Empty UI shell**: header with application name and language switcher, empty main area,
  footer with the API version, not-found page.
- **UC-27 Switch UI language**: react-i18next with the three locales, browser detection, persisted
  choice, `<html lang>` kept in sync, CI check that every locale has every key.
- **Containers** (ADR-0006): Dockerfiles for `api` and `web` (static SPA + reverse proxy),
  `compose.yaml` with `api`, `web`, `db` (PostgreSQL), `storage` (MinIO), `mail` (Mailpit);
  `.env.example` with placeholders only.
- **CI** (ADR-0006): GitHub Actions for backend, frontend, OpenAPI drift, Playwright E2E against the
  compose stack, container image build, secret scanning, dependency review/audit, CodeQL;
  Dependabot configuration.
- **Repository hygiene**: MIT `LICENSE` (ADR-0010), root `README.md`, `docs/development.md`,
  `.editorconfig`, `.gitignore` additions, third-party license register (incl. QuestPDF check
  required by ADR-0008/0010).

## Capabilities

### New Capabilities
- `platform`: runtime platform shared by every capability — health and system info endpoints,
  error/logging/configuration/security-header behaviour, request localisation, synthetic seed
  guard, UI shell with language switching (UC-27), generated API client, module boundaries and
  CI quality gates.

### Modified Capabilities
<!-- none: no specs exist yet -->

## Non-goals

- Authentication, users, roles, anti-forgery and comparsa scoping (BR-12, SEC-03/04, ADR-0004) —
  change #3 `add-identity-access`. Until then the API exposes only anonymous, non-personal endpoints.
- Design tokens, Tailwind, shadcn/ui, `components/app/` composites, Storybook and the
  `components/ui` import guardrail (ADR-0009) — change #2 `add-design-system`. The shell uses
  minimal plain CSS and is restyled there.
- Persisting the preferred language on the user profile (ADR-0007) — needs users (#3); this change
  persists it in the browser.
- Any domain module, table, migration or domain seeder; audit logging (SEC-05, first writes in #3).
- Using object storage or email from the API (containers only; used in #6 and #14).
- Staging/production hosting, deployment pipeline, TLS termination, backups (SEC-07) and error
  tracking service — pending hosting assessment before go-live (`docs/mvp.md`).
- Offline capture (UC-21); only the installable PWA baseline is added (NFR-03).

## Impact

- **New code**: `backend/` (.NET solution: API host, shared kernel, test projects), `frontend/`
  (Vite app, unit tests, Playwright E2E), `docker/`, `compose.yaml`, `.github/` (workflows,
  Dependabot).
- **New dependencies**: .NET (ASP.NET Core, OpenAPI, Npgsql, xUnit, Testcontainers, coverlet),
  npm (React, Vite, React Router, TanStack Query, react-i18next, i18next-browser-languagedetector,
  orval, vite-plugin-pwa, ESLint plugins, Prettier, Vitest, Testing Library, MSW, axe, Playwright).
  Images: .NET runtime with ICU, nginx, PostgreSQL, MinIO, Mailpit. EF Core, Bogus and date-fns are
  added by the first change that needs them.
- **Docs**: `README.md`, `docs/development.md`, `docs/third-party-licenses.md`, `docs/README.md`
  index, `LICENSE`.
- **Implements**: UC-27; ADR-0001, 0002, 0006, 0007, 0010, 0011 (foundations); NFR-01, 02, 03, 09,
  12, 13, 14; SEC-11. No business rules (BR-xx) are touched.
