# 0006. Docker for development and deployment

- Status: Accepted
- Date: 2026-09-30

## Context

Round 4: development locally with Docker and deployment as containers; the Federation's
existing hosting will be assessed later. The repository is public.

## Decision

- `docker compose` for local development: `api`, `web` (static SPA + reverse proxy), `db`
  (PostgreSQL), `storage` (MinIO), `mail` (Mailpit to capture emails).
- Production images built in CI (**GitHub Actions**) and published to a container registry;
  deployed with docker compose on the target host behind a TLS reverse proxy.
- Configuration and secrets **only via environment variables** / secret files outside git;
  `.env.example` committed with placeholders.
- Environments: `development` (local), `staging` and `production`; staging uses synthetic data only (NFR-13).
- CI: build, tests, lint, dependency and secret scanning on every pull request.
- Backups: scheduled PostgreSQL dump + bucket sync to a separate EU location, with restore test.

## Consequences

- Same topology in every environment; the host only needs Docker.
- **Risk:** if the Federation's current hosting is shared hosting without container support, a
  small EU VPS will be needed (low monthly cost). To be assessed before go-live.

## Alternatives considered

- **PaaS-specific deployment** (Azure App Service, Fly.io…) — possible later; containers keep that door open.
