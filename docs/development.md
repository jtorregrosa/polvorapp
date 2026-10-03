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

The one-shot `api-migrate` service applies the database migrations and creates the photo bucket
(`STORAGE_BUCKET`) before `api` starts; the API itself never migrates on startup.

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
export Email__SmtpHost=localhost Email__SmtpPort=1025 Email__Security=None
export Email__From=no-reply@polvorapp.example App__PublicBaseUrl=http://localhost:5173
export Storage__ServiceUrl=http://localhost:9000 Storage__Bucket=polvorapp-photos
export Storage__AccessKey=polvorapp Storage__SecretKey=local-only-change-me
dotnet watch --project backend/src/PolvorApp.Api run --urls http://localhost:5080
```

PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__Postgres = 'Host=localhost;Database=polvorapp;Username=polvorapp;Password=local-only-change-me'
$env:Email__SmtpHost = 'localhost'; $env:Email__SmtpPort = '1025'; $env:Email__Security = 'None'
$env:Email__From = 'no-reply@polvorapp.example'; $env:App__PublicBaseUrl = 'http://localhost:5173'
$env:Storage__ServiceUrl = 'http://localhost:9000'; $env:Storage__Bucket = 'polvorapp-photos'
$env:Storage__AccessKey = 'polvorapp'; $env:Storage__SecretKey = 'local-only-change-me'
dotnet watch --project backend/src/PolvorApp.Api run --urls http://localhost:5080
```

Apply the migrations first (and again whenever a change adds one):

```bash
dotnet run --project backend/src/PolvorApp.Api migrate
```

UI on <http://localhost:5173>:

```bash
cd frontend && npm ci && npm run dev
```

### Object storage (photos)

Photos live in a private S3-compatible bucket (ADR-0005): MinIO locally, any S3-compatible
provider in an EU region elsewhere. The browser never talks to it; the API streams photos after
its access checks. Settings (`Storage__*`, validated at startup, values never logged):

| Setting | Meaning |
|---|---|
| `Storage__ServiceUrl` | S3 endpoint; https outside `Development` and `Testing`, unless the storage runs on the same machine (loopback) |
| `Storage__Bucket` | Bucket name; `migrate` creates it when it is missing |
| `Storage__AccessKey`, `Storage__SecretKey` | Credentials |
| `Storage__Region` | Signing region, default `us-east-1` |
| `Storage__ForcePathStyle` | `true` (default) for MinIO and most providers |
| `Storage__ServerSideEncryption` | `None` (default, rely on bucket encryption) or `Aes256` |
| `Storage__SweepEnabled` | `true` (default); `false` while restoring backups |
| `Storage__SweepIntervalMinutes` | Orphan sweep interval, default 60 |
| `Storage__BootstrapMaxWaitSeconds` | How long `migrate` waits for the storage, default 30 |

Locally the API uses the MinIO **root** credentials from `.env`; that is acceptable only on a
developer machine. The MinIO console (<http://localhost:9001>, root credentials) shows the stored
objects. Real environments:

- use a key limited to the bucket (get, put, delete and list objects; create bucket only if
  `migrate` should create it);
- turn on encryption at rest, keep the bucket private (no public policy or ACL), and leave
  **versioning off** (or expire non-current versions within a day): with versioning, a delete
  only hides a photo, which breaks erasure (BR-14);
- give every environment its **own bucket**: the orphan sweep deletes objects that the
  environment's database does not reference;
- set `Storage__SweepEnabled=false` while restoring the database or the bucket from a backup, and
  turn it on again once both come from the same point in time.

The sweep refuses (and logs an Error) a run that would delete more than 100 objects and more than
half of what it scanned, because that looks like a database and a bucket that do not belong
together.

### Synthetic seed data

```bash
docker compose run --rm api-seed                     # option A
dotnet run --project backend/src/PolvorApp.Api seed  # option B (also set Seed__UserPassword and Seed__AuthenticatorKey)
```

`api-seed` is in the `seed` profile, so `docker compose up --build` does not rebuild it: after
backend changes run `docker compose --profile seed build api-seed` first.

Seeding runs only in `Development`, `Staging` and `Testing` and never uses real data (SEC-11).
Outside `Development` and `Testing` each seeder first refuses a database that holds anything it
would not have created (users, comparsas, weapon models, assignments, arquebusiers, editions or orders), so staging never mixes
synthetic and real data (NFR-13).
Each module registers its own `IDataSeeder`; randomness derives from `SyntheticData.RandomSeed`,
so every run produces the same data. Seeding is not transactional: after a failed run, reset the
database (`docker compose down -v`) and seed again.

The catalogue seed adds fictional comparsas and a weapon catalogue; real comparsas and models are
entered by an Admin in production:

| Comparsa | Side | State | FiringChiefs |
|---|---|---|---|
| Comparsa Sintética Norte | Christian | active | Jefe Sintético Uno, Jefa Sintética Dos |
| Comparsa Sintética Sur | Moorish | active | Jefe Sintético Uno |
| Comparsa Sintética Este | Christian | active | — |
| Comparsa Sintética Oeste | Moorish | inactive | — |

Weapon models: trabuco (Christian) and arcabuz (Moorish) in every handedness and size, one of them
inactive ("ARCABUZ MORO ZURDO (PEQUEÑO)"), plus a non-rentable "PISTOLA" without attributes.

The registry seed adds 14 fictional arquebusiers ("Arcabucero Sintético Uno" … "Arcabucera
Sintética Catorce"): six in Norte, four in Sur, three in Este and one in the inactive Oeste. Their DNI/NIE are valid but built
from very low numbers unlikely to be in use (`00000001R`, `X0000005M`…), their emails use `@polvorapp.example`, and
their license dates are relative to the seed date, so the mix stays the same over time: active and
reserve, AE and A-PROF licenses that are valid, expired or pending, no license, course done and not
done. Five owned weapons (guides `SINT-0001` … `SINT-0005`) cover a trabuco, arcabuces, a pistol and
one weapon of the inactive model. Their phones (`+34 600 000 0NN`) are in the Spanish mobile range,
which has no reserved fictional numbers: PolvorApp never calls or messages them.

The edition seed (`add-festival-editions`) adds three festival editions. Their dates are relative
to the seed date:

| Edition | Status | Orders | Dates |
|---|---|---|---|
| Last year's | Closed | closed | festival 22–25 April, orders 10 January – 10 February |
| Current (the festival's year) | In progress | open | orders from 14 days before the seed date to 30 days after; festival 60 to 63 days after |
| Next year's | Draft | closed | festival 22–25 April, no order window yet |

All three have the same invented prices (48.00 € per kg of powder, 3.75 € a caps box, 25.00 € a
weapon rental, 5.00 € a flask rental). They also offer the seeded trabucos and active arcabuces
for rental. The current edition has four synthetic milestones and the closed one has one.

The order seed (`add-comparsa-orders`) adds four comparsa orders, copying names, DNI/NIE and
weapons from the seeded registry:

| Edition | Comparsa | Status | Entries |
|---|---|---|---|
| Last year's | Norte | Validated | arquebusiers Uno, Dos and Cuatro (reserve), plus one entry of "Arcabucero Sintético Histórico", no longer in the registry |
| Last year's | Sur | Validated | Seis, Siete and Ocho (reserve) |
| Current | Norte | Submitted (attested by Jefa Sintética Dos) | Uno to Cinco and Catorce |
| Current | Sur | Draft | Seis, Siete and Ocho; Nueve is "not in the order" |
| Current | Este | not prepared | — |

Together they cover every weapon source (owned, rental, loan, none), every flask option and both
caps types, reserve entries, an active entry without powder (Dos, a shooter) and one without a
weapon (Tres, a powder carrier). Cinco borrows Seis's owned weapon, from Sur; Catorce borrows from
an external owner ("Propietaria Externa Sintética", DNI `00000092T`, guide `SINT-EXT-0001`). Tres,
Cinco and Catorce have no active entry last year, so they show as first year.

The registry is seeded unlocked. Seeded dates are never refreshed: on a database seeded weeks ago,
the current edition's order window may already have passed. Reset the database
(`docker compose down -v`) and seed again for fresh dates.

### Signing in locally

The seed creates synthetic users on the reserved `.example` domain (never real people):

| Email | Role | State |
|---|---|---|
| `admin@polvorapp.example` | Admin | active |
| `jefe.uno@polvorapp.example` | FiringChief of Norte and Sur | active |
| `jefa.dos@polvorapp.example` | FiringChief of Norte | active |
| `invitada@polvorapp.example` | FiringChief | invited |
| `desactivada@polvorapp.example` | FiringChief | deactivated |

Every active user signs in with `SEED_USER_PASSWORD` and a code from an authenticator app set up
with `SEED_AUTHENTICATOR_KEY` (both in `.env`; the published placeholders work only in
`Development`). All seeded users share the key, so one entry in the app serves them all. Scan this
QR code with any TOTP app (Google Authenticator, Microsoft Authenticator, FreeOTP…); it holds the
placeholder key from `.env.example`:

![Authenticator setup for the local seed users](images/seed-authenticator-qr.png)

Or add the key by hand as a time-based, 6-digit, 30-second account. If you changed
`SEED_AUTHENTICATOR_KEY`, turn this URI into a QR code with your key instead:
`otpauth://totp/PolvorApp%20local:usuarios-seed?secret=<KEY>&issuer=PolvorApp%20local`.
A code is accepted once per user and within 30 seconds of the server's clock; five wrong codes
lock the account for 15 minutes. Invitations and password-reset emails land in Mailpit
(<http://localhost:8025>); links point at `App__PublicBaseUrl`.

### Host commands

The API image runs one command instead of the web server when given a verb:

| Command | What it does |
|---|---|
| `migrate` | Applies every module's migrations and creates the storage bucket if it is missing (run by the `api-migrate` service). |
| `seed` | Creates the synthetic data above (`Development`, `Staging`, `Testing` only). |
| `create-admin --email <email> --name <name> [--locale es-ES\|ca-ES-valencia\|en]` | Invites the first Admin on a new installation (email with the invitation link). Refused once an Admin can sign in; run again to resend the invitation to the same invited Admin. |

```bash
docker compose run --rm api create-admin --email admin@example.org --name "Admin name"
```

Exit codes: `0` success, `1` failure, `2` unknown command or invalid arguments, `130` cancelled.

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

- Integration tests share one PostgreSQL, Mailpit and MinIO container per run, and test classes run
  in parallel. Each test gets a database of its own: a copy of a template migrated once per run
  (`PostgresFixture.CreateMigratedDatabaseAsync`), or an empty one for migration tests. Tests that
  list stored objects use a bucket of their own, and an address whose mail a test reads or counts
  is used by that test class only.
- New modules follow [`backend/src/Modules/README.md`](../backend/src/Modules/README.md);
  architecture tests enforce the module boundaries.
- Persistence: one EF Core `DbContext` and one PostgreSQL schema per module (see the modules
  README). Add a migration from `backend/` after `dotnet tool restore` (the `dotnet-ef` version
  in `dotnet-tools.json` moves together with EF Core):

  ```bash
  dotnet tool restore
  dotnet ef migrations add <Name> --project src/Modules/<Module>/PolvorApp.<Module>     --startup-project src/Modules/<Module>/PolvorApp.<Module> --output-dir Persistence/Migrations
  ```

  Apply them with the host `migrate` command (`docker compose run --rm api-migrate` or
  `dotnet run --project src/PolvorApp.Api migrate`); tests migrate their Testcontainers database
  the same way.
- The database must use a **UTF-8 ctype** (the default of the `postgres` images, e.g.
  `en_US.utf8`), never `C` or `POSIX`: case-insensitive uniqueness of comparsa names and weapon
  labels relies on `lower()`, which with `C`/`POSIX` folds ASCII only, so "PEQUEÑO" and "pequeño"
  would count as different. Check a hosted database with
  `SELECT datctype FROM pg_database WHERE datname = current_database();`.
- Configuration comes only from environment variables (`ConnectionStrings__Postgres`,
  `Email__SmtpHost`, `Email__SmtpPort`, `Email__Security` (`None` | `StartTls` | `SslOnConnect`),
  `Email__From`, optional `Email__Username`/`Email__Password` and `Email__TimeoutSeconds`,
  `App__PublicBaseUrl`, …). Outside `Development` and `Testing` the API requires TLS for SMTP and an
  https `App__PublicBaseUrl`. The
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
npm run lint            # ESLint (no-literal-string and design-system guardrails)
npm run typecheck
npm run check-i18n      # every locale has every key and the same placeholders
npm run test            # Vitest (includes the component catalogue checks)
npm run test:coverage   # with the 80 % gate (vendored src/components/ui and stories excluded)
npm run build
npm run format          # Prettier
npm run storybook       # component catalogue, http://localhost:6006
npm run build-storybook # static catalogue in storybook-static/ (CI builds it)
npm run docs:design     # regenerate docs/design/tokens.md and status.md from the code
```

- Feature code lives in `src/features/<capability>/`; shared composites in
  `src/components/app/` (ADR-0009). Call the API only through the generated hooks.
- Every user-facing text is a translation key. See [i18n workflow](#i18n-workflow).
- UI work follows the [design guide](design/README.md).

### Design system

- **Tokens**: colours, type, spacing, sizes, elevation, widths and motion live only in
  `src/styles/tokens.css` (light `:root` and `.dark`); Tailwind utilities are generated from them.
  `src/styles/contrast.test.ts` checks every declared pair, the hover states and the night sidebar
  against WCAG 2.2 AA in both themes. After changing tokens or `src/components/app/status.ts`, run
  `npm run docs:design`; the tests fail while the generated guide pages are out of date.
- **Primitives and composites**: shadcn/ui primitives are in `src/components/ui/` (added with the
  shadcn CLI, `components.json`; keep local edits minimal and listed in the design guide).
  `src/components/ui/primitives-guard.test.ts` rejects translucent focus rings and hover colours
  in them, because ESLint exempts that folder.
  Features and the shell use only the composites in `src/components/app/`.
- **Guardrails** (ESLint, `eslint.config.js`): no raw palette colours, arbitrary values or opacity
  on semantic tones outside `src/components/ui/`; no `@/components/ui` imports and no `style`
  attributes in features and the shell; Tailwind correctness rules everywhere.
  `scripts/eslint-guardrails.test.mjs` proves each rule still rejects and accepts what it should.
- **Catalogue**: every composite has a sibling `*.stories.tsx`. `catalogue.test.tsx` renders every
  story in the three languages and both themes and fails on axe violations, render errors,
  untranslated keys, or a composite without stories.
- **Theme**: light, dark or system, stored under `polvorapp.theme`. `public/theme-init.js` applies
  it before the first paint (an external script, so the CSP keeps `script-src 'self'`).

## End-to-end tests

Playwright runs against the compose stack with the synthetic users seeded. The suite signs in many
times from one address, so raise the per-address sign-in limits for the run (CI does the same):

```bash
RATE_LIMIT_AUTH_PER_MINUTE=300 RATE_LIMIT_AUTH_EMAIL_PER_15_MINUTES=100 RATE_LIMIT_PERSONAL_DATA_WRITES_PER_MINUTE=600 RATE_LIMIT_IMAGE_UPLOADS_PER_MINUTE=200 RATE_LIMIT_SPREADSHEET_IMPORTS_PER_MINUTE=100 docker compose up -d --build --wait
docker compose run --rm api-seed
cd frontend && npx playwright install --with-deps chromium firefox webkit   # once
npm run e2e
```

A `setup` project signs the seeded Admin and a FiringChief in once and saves their sessions in
`frontend/e2e/.auth/` (git-ignored); specs start as the Admin. The `dates-firefox` and
`dates-webkit` projects run only `e2e/dates.spec.ts`, because native date fields differ per engine.
Playwright's WebKit builds have no Safari-like date field (a text box on Windows, a field without
editable segments on Linux), so WebKit only checks the empty required date; typing a full and a
partial date in Safari (macOS and iOS) is a manual release check. Journeys that need a fresh user
invite one through the UI, read the link from Mailpit's API and enrol with a computed TOTP code
(`e2e/identity.ts`), so runs never collide on a code.

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
E2E on the compose stack, container image builds, secret scanning and dependency review. The gates
run side by side; the E2E job builds its images while it installs the browsers. On `main`,
`publish-images` pushes the images to GHCR after all gates pass. `codeql.yml` runs CodeQL; Dependabot keeps dependencies and actions current.

A follow-up push to an open pull request that only touches `openspec/` and `docs/` (outside
`docs/design/`, which the frontend tests read) skips the code gates, such as the OpenSpec archive
commit. This happens only when the previous commit passed backend, frontend, E2E and images; the
`changes` job decides it and the skipped gates count as passed for the required checks. Secret
scanning always runs, and `main` always runs everything. CodeQL ignores pull requests that only
touch `openspec/` and `docs/`.

### Dependency updates

Dependabot opens weekly pull requests (minor and patch updates grouped). Major versions that a
peer dependency or the runtime policy does not allow yet are listed with their reason under
`ignore` in `.github/dependabot.yml`; remove the entry once the reason no longer holds (for
example when Node 26 becomes LTS on 2026-10-28, or when `eslint-plugin-i18next` supports ESLint 10).

### Repository settings (maintainer, once)

CI cannot configure these; set them in GitHub:

1. **Branch protection** on `main`: require a pull request and the checks `backend`, `frontend`,
   `e2e`, `images` and `secrets`; disallow force pushes.
2. **Secret scanning** with **push protection** (Settings → Code security). Enabled.
3. **GHCR packages** `polvorapp-api` and `polvorapp-web`: after the first push from `main`, set
   their visibility to **public** (Package settings → Change visibility), so hosts can pull
   without credentials. Images never contain secrets.
