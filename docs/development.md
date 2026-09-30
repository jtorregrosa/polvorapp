# Development Guide

How to run, test and change PolvorApp locally. Architecture and decisions: [`adr/`](adr/README.md);
workflow (OpenSpec, TDD, reviews): [`../.claude/rules/polvorapp-workflow.md`](../.claude/rules/polvorapp-workflow.md).

## Prerequisites

| Tool | Version | Used for |
|---|---|---|
| Docker (Desktop or Engine) with Compose | recent | local stack, backend integration tests (Testcontainers), E2E |
| .NET SDK | 10.0 (`backend/global.json`) | backend |
| Node.js | 24.15+ (`frontend/.nvmrc`) | frontend |

## Repository layout

```
backend/            .NET solution: API host (platform), shared kernel, modules, tests
contracts/          committed OpenAPI document (source of the frontend client)
frontend/           React SPA, unit tests, Playwright E2E (e2e/), nginx config, Dockerfile
scripts/            repository scripts (coverage gate, ECC sync)
compose.yaml        local environment; .env.example holds its placeholder settings
```

## Local environment

### Option A — everything in containers

For local use only: it runs in `Development` with placeholder credentials. Deployment is defined
before go-live (ADR-0006).

```bash
cp .env.example .env          # once; placeholders only, never commit .env
docker compose up --build     # UI + API on http://localhost:8080
```

| Service | Address (loopback only) |
|---|---|
| Web (UI + `/api`) | <http://localhost:8080> |
| PostgreSQL | `localhost:5432` (credentials from `.env`) |
| MinIO API / console | <http://localhost:9000> / <http://localhost:9001> |
| Mailpit (captured email) | <http://localhost:8025> (SMTP `localhost:1025`) |

### Option B — infrastructure in containers, code with hot reload

```bash
docker compose up db storage mail
```

API on port 5080 (the Vite dev server proxies `/api` there). Use the database values from your
`.env` (the examples show the `.env.example` defaults). Bash:

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Postgres="Host=localhost;Database=polvorapp;Username=polvorapp;Password=local-only-change-me"
dotnet watch --project backend/src/PolvorApp.Api run --urls http://localhost:5080
```

PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__Postgres = 'Host=localhost;Database=polvorapp;Username=polvorapp;Password=local-only-change-me'
dotnet watch --project backend/src/PolvorApp.Api run --urls http://localhost:5080
```

UI on <http://localhost:5173>:

```bash
cd frontend && npm ci && npm run dev
```

### Synthetic seed data

```bash
docker compose run --rm api seed                     # option A
dotnet run --project backend/src/PolvorApp.Api seed  # option B (same environment variables)
```

Seeding runs only in `Development`, `Staging` and `Testing` and never uses real data (SEC-11).
Each module registers its own `IDataSeeder`; randomness derives from `SyntheticData.RandomSeed`,
so every run produces the same data. Seeding is not transactional: after a failed run, reset the
database (`docker compose down -v`) and seed again.

## Backend

Run from `backend/` (its `global.json` selects the SDK and the Microsoft Testing Platform runner).

```bash
dotnet build PolvorApp.slnx
dotnet format PolvorApp.slnx --verify-no-changes
dotnet test --solution PolvorApp.slnx                       # needs Docker (Testcontainers)
dotnet test --solution PolvorApp.slnx --coverlet --coverlet-output-format cobertura \
  --coverlet-include "[PolvorApp.*]*" --coverlet-exclude "[*Tests]*" --results-directory TestResults
node ../scripts/check-coverage.mjs TestResults 80          # 80 % line gate
```

- New modules follow [`backend/src/Modules/README.md`](../backend/src/Modules/README.md);
  architecture tests enforce the module boundaries.
- Configuration comes only from environment variables (`ConnectionStrings__Postgres`, …). The
  API refuses to start when a required setting is missing, naming the setting.
- Logs are JSON on stdout and never include query strings, bodies or personal data (NFR-12).

### API contract

Building `PolvorApp.Api` regenerates [`contracts/openapi.json`](../contracts/openapi.json).
Commit it together with the endpoint change: CI fails when the committed document differs from
the generated one. In Development the document is also served at `/api/openapi/v1.json`.

## Frontend

Run from `frontend/`. The API client is generated from `contracts/openapi.json` by orval into
`src/api/generated/` (git-ignored); `dev`, `build`, `typecheck`, `lint` and `test` regenerate it.

```bash
npm ci
npm run dev             # http://localhost:5173
npm run lint            # ESLint (includes the no-literal-string rule)
npm run typecheck
npm run check-i18n      # every locale has every key and the same placeholders
npm run test            # Vitest
npm run test:coverage   # with the 80 % gate
npm run build
npm run format          # Prettier
```

- Feature code lives in `src/features/<capability>/`; shared composites in
  `src/components/app/` (ADR-0009). Call the API only through the generated hooks.
- Every user-facing text is a translation key. See [i18n workflow](#i18n-workflow).

## End-to-end tests

Playwright runs against the compose stack:

```bash
docker compose up -d --build --wait
cd frontend && npx playwright install --with-deps chromium   # once
npm run e2e
```

## i18n workflow

1. Add the key to `frontend/src/i18n/locales/es-ES/<namespace>.json`, then to `ca-ES-valencia`
   and `en` (same key, same `{{placeholders}}`). Keys are English and namespaced per feature.
2. `npm run check-i18n` must pass; TypeScript rejects unknown keys.
3. API texts (e.g. problem-details titles) live in `backend/src/PolvorApp.Api/**/SharedResources*.resx`;
   Valencian uses the `.ca.resx` file.
4. Valencian texts are reviewed by someone familiar with festival vocabulary; mention new
   Valencian texts in the pull request description.

## Continuous integration

`.github/workflows/ci.yml` runs on every pull request and on `main`: backend (format, build,
tests + coverage, contract drift), frontend (audit, lint, types, i18n, tests + coverage, build),
E2E on the compose stack, container image builds, secret scanning and dependency review. On
`main`, `publish-images` pushes the images to GHCR after all gates pass. `codeql.yml` runs CodeQL; Dependabot keeps dependencies and actions current.

### Repository settings (maintainer, once)

CI cannot configure these; set them in GitHub:

1. **Branch protection** on `main`: require a pull request and the checks `backend`, `frontend`,
   `e2e`, `images` and `secrets`; disallow force pushes.
2. **Secret scanning** with **push protection** (Settings → Code security).
3. **GHCR packages** `polvorapp-api` and `polvorapp-web`: after the first push from `main`, set
   their visibility to **public** (Package settings → Change visibility), so hosts can pull
   without credentials. Images never contain secrets.
