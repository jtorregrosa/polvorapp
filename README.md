# PolvorApp

Management portal for the arquebusiers of the Moros y Cristianos festival of San Vicente del
Raspeig: the Federation and its ~20 comparsas keep one source of truth for arquebusiers, licenses,
yearly orders, exports and distribution, instead of spreadsheets and forms.

> Status: in development. Platform, design system and accounts (invitations, sign-in with two-step
> verification, user management) are in place; the arquebusier registry comes next ([MVP plan](docs/mvp.md)).

## Quick start

Requires Docker.

```bash
cp .env.example .env
docker compose up --build
docker compose run --rm api-seed   # synthetic users, in another terminal once the stack is up
```

Open <http://localhost:8080> and sign in as `admin@polvorapp.example` with `SEED_USER_PASSWORD`
and a code from an authenticator app set up with `SEED_AUTHENTICATOR_KEY` (both in `.env`; see
[Signing in locally](docs/development.md#signing-in-locally)). Emails are caught by Mailpit at
<http://localhost:8025>. The API is served on the same origin under `/api`
(`/api/health/ready`, `/api/system/info`).

## Documentation

- [Development guide](docs/development.md) — local setup, commands, tests, CI.
- [Documentation index](docs/README.md) — vision, domain, use cases, compliance, ADRs, MVP plan.
- Specifications and changes live in [`openspec/`](openspec/) (spec-driven workflow).

## Stack

ASP.NET Core (.NET 10) modular monolith + PostgreSQL · React + TypeScript + Vite SPA ·
Docker Compose · GitHub Actions. Decisions and rationale: [`docs/adr/`](docs/adr/README.md).

## Data protection

The repository is public. It never contains real personal data or secrets: all test and demo
data is synthetic, and configuration comes from environment variables.

## License

[MIT](LICENSE) © 2026 Jorge Torregrosa Lloret. Third-party licenses:
[docs/third-party-licenses.md](docs/third-party-licenses.md).
