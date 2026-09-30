# 0011. Frontend: React SPA (Vite), installable PWA

- Status: Accepted
- Date: 2026-09-30
- Supersedes: [0003](0003-frontend-angular.md)

## Context

The UI is an internal management tool: many forms and data tables, role-based navigation,
three languages, responsive for phones (photo capture) and, later, offline capture on
distribution day (NFR-03). Code is written with heavy AI assistance (Claude Code + ECC), so the
ecosystem where generated code is most reliable and idiomatic matters more than the maintainer's
framework preference. React's usual drawback — choosing many libraries — is neutralised by fixing
them here once.

## Decision

A single-page application in **React + TypeScript (strict)** built with **Vite**, with this fixed toolset:

| Concern | Choice |
|---|---|
| Routing | React Router |
| Server state | TanStack Query |
| API client | Generated from the backend OpenAPI document (orval) — no hand-written DTOs |
| Forms & validation | React Hook Form + Zod (schemas mirror business rules BR-xx) |
| Tables | TanStack Table (wrapped in an app-level `DataTable`, see ADR-0009) |
| UI / design system | shadcn/ui + Tailwind CSS v4 (ADR-0009) |
| i18n | react-i18next (ADR-0007) |
| PWA / offline | vite-plugin-pwa (Workbox) now; IndexedDB for offline capture later (UC-21) |
| Dates | date-fns (locales `es`, `ca`, `en`) |
| Unit / component tests | Vitest + Testing Library |
| E2E tests | Playwright |
| Lint / format | ESLint (incl. jsx-a11y, Tailwind plugin) + Prettier |

- Served as static files from the same origin as the API behind the reverse proxy, so
  authentication cookies stay first-party (ADR-0004).
- Code organised **by feature = OpenSpec capability** (`src/features/<capability>/`), mirroring the backend modules (ADR-0001).
- No global client-state library unless a real need appears; server state lives in TanStack Query.

## Consequences

- Lighter and more flexible than Angular; very reliable AI-generated code.
- Consistency depends on the design-system guardrails of ADR-0009 and this fixed toolset;
  adding a library that overlaps with one above requires a new ADR.

## Alternatives considered

- **Angular** (ADR-0003) — batteries included, but heavier and less flexible; see supersession note.
- **Next.js / SSR** — unnecessary: internal authenticated app, no SEO, and the API is .NET.
