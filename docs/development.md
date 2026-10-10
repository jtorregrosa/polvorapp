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
would not have created (users, comparsas, weapon models, assignments, arquebusiers, editions or
orders), so staging never mixes synthetic and real data (NFR-13). Each module registers its own
`IDataSeeder`; randomness derives from `SyntheticData.RandomSeed`, so every run produces the same
data. Seeding is not transactional: after a failed run, reset the database
(`docker compose down -v`) and seed again.

Everything in the seed is invented or generated (`realistic-seed-data`). The comparsa names are
plausible festival names, but none belongs to San Vicente del Raspeig. People's names are
combinations from Alicante frequency lists, so any match with a real person is coincidental. DNI/NIE
come from ranges with no evidence of use (`99xxxxxx`, `Z9xxxxxx`), emails use the reserved
`polvorapp.example` domain, and phones are Spanish mobiles that PolvorApp never calls or messages.

#### Datasets

`Seed__Dataset` (compose: `SEED_DATASET`, default `Full`) chooses what is created:

| Dataset | What | Used by |
|---|---|---|
| `Scenarios` | the fixed cases below: 4 comparsas, 5 users, 14 arquebusiers, 4 orders | backend tests, CI's E2E job, `dotnet run … seed` without the setting |
| `Full` | the scenarios plus 16 comparsas with one FiringChief each, about 450 arquebusiers with photos, and their orders; about a minute | `docker compose run --rm api-seed`, staging |

Running `Full` after `Scenarios` adds the population. Going back from `Full` to `Scenarios` needs a
reset (`docker compose down -v`), because seeded rows are never removed.

#### Comparsas and users

The catalogue seed adds invented comparsas and a weapon catalogue; real comparsas and models are
entered by an Admin in production. The scenarios:

| Comparsa | Side | State | FiringChiefs | Logo |
|---|---|---|---|---|
| Cruzados | Christian | active | Joan Moltó Sala, Elena Verdú Ivorra | dark (needs the light tile) |
| Abencerrajes | Moorish | active | Joan Moltó Sala | none |
| Hospitalarios | Christian | active | — | yes |
| Zegríes | Moorish | inactive | — | yes |

The full dataset adds Tercios, Ballesteros, Corsarios, Labradores, Mozárabes, Caballeros de Sant
Jordi, Almirantes and Guardia del Rey (Christian), and Almohades, Nazaríes, Mudéjares, Bereberes,
Beduinos, Kábilas, Califas and Sarracenos (Moorish). Each has its own generated FiringChief, who
signs in like the seeded users below.

Weapon models: trabuco (Christian) and arcabuz (Moorish) in every handedness and size, one of them
inactive ("ARCABUZ MORO ZURDO (PEQUEÑO)"), plus a "PISTOLA" without attributes, kept non-rentable to cover that case (any kind may be rentable, BR-07).

#### Arquebusiers

The scenario arquebusiers. License dates are relative to the seed date, so the mix stays the same
over time: active and reserve, AE and A-PROF licenses that are valid, expiring, expired or pending,
no license, course done and not done.

| # | Name | DNI/NIE | Comparsa |
|---|---|---|---|
| 1 | Vicent Sempere Llorens | 99000001 | Cruzados |
| 2 | Amparo Pastor Gomis | 99000002 | Cruzados |
| 3 | Pau Alberola Navarro | 99000003 | Cruzados |
| 4 | Remedios Lledó Pérez (reserve) | 99000004 | Cruzados |
| 5 | Youssef El Amrani | Z9000005 | Cruzados |
| 6 | Mari Carmen Ferrándiz Soler | 99000006 | Abencerrajes |
| 7 | Josep Ramon Candela Martínez | 99000007 | Abencerrajes |
| 8 | Pepa Mira Carbonell (reserve) | 99000008 | Abencerrajes |
| 9 | Toni Baeza Ripoll | 99000009 | Abencerrajes |
| 10 | Ioana Popescu | Z9000010 | Hospitalarios |
| 11 | Rafael Climent Esteve | 99000011 | Hospitalarios |
| 12 | Àlex Beltrà Riquelme (reserve) | 99000012 | Hospitalarios |
| 13 | Francisco Asensi Mollà (reserve) | 99000013 | Zegríes |
| 14 | Laia Sempere Pastor (16, four warnings) | 99000014 | Cruzados |

Five owned weapons (guides `GP-000001` … `GP-000005`) cover a trabuco, arcabuces, a pistol and one
weapon of the inactive model. The full dataset adds 15–40 people per added comparsa, with ages of
16–75, licenses and courses consistent with the age, owned weapons of their side, and a minority
showing each compliance warning.

#### Images

The ID photos and comparsa logos are committed generated images in `backend/synthetic-data/` (see
its `NOTICE.md`), copied to the API's output as `SyntheticData/`:

- **Faces.** 400 faces of people who do not exist, matched to each arquebusier's gender and age.
- **Emblems.** 19 invented heraldic emblems without text.
- **License photos.** Drawn as specimens from the arquebusier's own data, with a "MUESTRA – SIN
  VALIDEZ" watermark.

To regenerate the images (Z-Image Turbo through Runpod's public endpoint, about USD 0.005 an image),
run `npm ci` in `frontend/` once, set `RUNPOD_API_KEY` and run:

```bash
node scripts/generate-seed-images.mjs --kind faces --dry-run   # prompts only, nothing is paid
node scripts/generate-seed-images.mjs --kind logos
```

Raw outputs are cached in the git-ignored `seed-assets/raw/`; delete a cached file to generate that
image again. The script replaces the committed folder only when every image succeeded. Before
committing, review new emblems by eye (no text, no likeness to a real emblem) and spot-check new faces
(ID-photo framing, no likeness to a known person); regenerate any doubtful one with another seed.

#### Editions

The edition seed (`add-festival-editions`) adds three festival editions. Their dates are relative
to the seed date:

| Edition | Status | Orders | Dates |
|---|---|---|---|
| Last year's | Closed | closed | festival 22–25 April, orders 10 January – 10 February |
| Current (the festival's year) | In progress | open | orders from 14 days before the seed date to 30 days after; festival 60 to 63 days after |
| Next year's | Draft | closed | festival 22–25 April, no order window yet |

All three have the same invented prices (48.00 € per kg of powder, 3.75 € a caps box, 25.00 € a
weapon rental, 5.00 € a flask rental). They also offer the seeded trabucos and active arcabuces
for rental, except the left-handed trabucos. The current edition has four milestones ("Convocatoria
de licencias", "Curso de formación", …) and the closed one has one.

#### Orders

The order seed (`add-comparsa-orders`) adds four scenario orders, copying names, DNI/NIE and weapons
from the seeded registry:

| Edition | Comparsa | Status | Entries |
|---|---|---|---|
| Last year's | Cruzados | Validated | arquebusiers 1, 2 and 4 (reserve), plus one entry of "Manuel Cerdà Boix", no longer in the registry |
| Last year's | Abencerrajes | Validated | 6, 7 and 8 (reserve) |
| Current | Cruzados | Submitted (attested by Elena Verdú Ivorra) | 1 to 5 and 14 |
| Current | Abencerrajes | Draft | 6, 7 and 8; 9 is "not in the order" |
| Current | Hospitalarios | not prepared | — |

Together they cover every weapon source (owned, rental, loan, none), every flask option and both
caps types, reserve entries, an active entry without powder (2, a shooter) and one without a weapon
(3, a powder carrier). 5 borrows 6's owned weapon, from Abencerrajes; 14 borrows from an external
owner ("Rosa Maria Agulló Vidal", DNI `99000092`, guide `GP-000092`). 3, 5 and 14 have no active
entry last year, so they show as first year.

The full dataset adds, for each added comparsa, a validated order of last year and, in the current
edition, 4 drafts, 5 submitted, 2 returned (with a reason) and 3 validated orders, with 2 comparsas
not prepared. Every order leaves one or two arquebusiers out. Reserves carry nothing; owners use their
weapon; the others rent the offered model of their side or borrow a team-mate's weapon.

The registry is seeded unlocked. Seeded dates are never refreshed: on a database seeded weeks ago,
the current edition's order window may already have passed. Reset the database
(`docker compose down -v`) and seed again for fresh dates.

### Signing in locally

The seed creates synthetic users on the reserved `.example` domain (never real people):

| Email | Role | State |
|---|---|---|
| `admin@polvorapp.example` (Inma Ruiz Bernabeu) | Admin | active |
| `jefe.uno@polvorapp.example` (Joan Moltó Sala) | FiringChief of Cruzados and Abencerrajes | active |
| `jefa.dos@polvorapp.example` (Elena Verdú Ivorra) | FiringChief of Cruzados | active |
| `invitada@polvorapp.example` (Sílvia Mora Castelló) | FiringChief | invited |
| `desactivada@polvorapp.example` (Jaume Cortés Puig) | FiringChief | deactivated |

The full dataset's FiringChiefs (one per added comparsa) use `nombre.apellido@polvorapp.example`
addresses; list them on the users page.

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
| `purge-audit` | Deletes the audit entries past their retention period at once (the API also does it daily, see "Audit retention"). |

```bash
docker compose run --rm api create-admin --email admin@example.org --name "Admin name"
```

Exit codes: `0` success, `1` failure, `2` unknown command or invalid arguments, `130` cancelled.

### Claude Code cloud sessions

Cloud sessions ([claude.ai/code](https://claude.ai/code)) run on an Ubuntu VM. It has Docker, but no
.NET SDK, and Node 22 first on `PATH`. `scripts/cloud-setup.sh` fills the gap, and the repository's
`.claude/settings.json` runs it as a SessionStart hook. Locally the hook does nothing, because it
only acts when `CLAUDE_CODE_REMOTE=true`. In the cloud the hook:

- installs the .NET SDK of `backend/global.json` and the Node of `frontend/.nvmrc`, if they are
  missing;
- puts that Node first on `PATH` for the session;
- starts `dockerd`, which the VM does not run at boot (Testcontainers and `docker compose` need it);
- when the proxy's CA bundle is present, sets `COMPOSE_FILE` so every `docker compose` command adds
  `compose.cloud.yaml`. The session's proxy inspects TLS, also from containers: that file hands
  the bundle (`PROXY_CA_BUNDLE`) to the image builds as the `proxy_ca` build secret, which both
  Dockerfiles trust for their downloads when it is present. It never enters an image layer, and
  builds without it (locally, CI) are unchanged;
- creates `.env` from `.env.example` with the settings of CI's E2E job (raised rate limits, short
  notification interval, the `Scenarios` dataset), unless `.env` exists;
- runs `npm ci` in `frontend/` whenever the lockfile changed.

Set up the cloud environment once:

1. **Network access:** *Custom*, keeping the defaults, plus `cgr.dev`, `*.cgr.dev` (MinIO image)
   and `builds.dotnet.microsoft.com` (.NET SDK).
2. **Environment variables:** `BASH_DEFAULT_TIMEOUT_MS=600000` and `BASH_MAX_TIMEOUT_MS=3600000`,
   because the backend suite takes about 25 minutes on one machine. Never put secrets here: everyone using the
   environment can read them.
3. **Setup script:** `bash scripts/cloud-setup.sh`. It installs the SDKs into the environment's
   cache, so later sessions start faster.

Containers do not survive between sessions. Start and seed the stack in each session, then run the
E2E suite with `npm run e2e:cloud`:

```bash
docker compose up -d --build --wait
docker compose run --rm api-seed
cd frontend && npm run e2e:cloud
```

`playwright.cloud.config.ts` runs the Chromium projects on the image's own Chromium
(`$PLAYWRIGHT_BROWSERS_PATH/chromium`), with no browser download. That build is older than
Playwright's, so it also leaves `select.spec.ts` (the customizable select's keyboard) to CI, which
runs it together with the Firefox and WebKit projects.

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

The whole suite takes a while: while working on one module, run its namespace only
(`dotnet test --project tests/PolvorApp.Api.Tests --filter-namespace "PolvorApp.Api.Tests.Distribution"`,
adding `--filter-namespace "PolvorApp.Api.Tests.Distribution.*"` for sub-namespaces) and leave the
full run to CI. CI splits it into three shards (`scripts/backend-test-shard.sh registry|catalog|rest`,
run from the repository root after a Release build) and merges their coverage.

- Integration tests share one PostgreSQL, Mailpit and MinIO container per run, and test classes run
  in parallel. Each test gets a database of its own: a copy of a template migrated once per run
  (`PostgresFixture.CreateMigratedDatabaseAsync`), or an empty one for migration tests. Each test
  also starts its own API host, so its start-up stays lean: test passwords are hashed with few
  iterations, and the host only ensures the bucket instead of running the migrate command. Tests that
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
  `App__PublicBaseUrl`, `Notifications__Enabled` (default `true`), `Notifications__DispatchIntervalSeconds`
  (1–3600, default 30), `Audit__PurgeEnabled` (default `true`), `Audit__RetentionYears` (5–100,
  default 5), `Audit__SecurityRetentionDays` (365–36500, default 365), …). Outside `Development` and `Testing` the API requires TLS for SMTP and an
  https `App__PublicBaseUrl`. The
  API refuses to start when a required setting is missing, naming the setting.
- **Environment variable or setting?** Anything secret or tied to the deployment is an environment
  variable: the database, SMTP host, port, security and credentials, the sender **address**
  (`Email__From`; only its address counts, since it depends on the mail domain's SPF/DKIM), the public
  address and the storage. What the Federation decides is a setting an Admin edits on the Settings page
  (`add-federation-settings`): its official and short names, public contact and website, the sender
  **name** and reply-to, and the reminder lead times. Settings never hold secrets or personal data.
- Logs are JSON on stdout and never include query strings, bodies or personal data (NFR-12).

### Notifications

The API sends the notification emails itself (`add-notifications`): a dispatcher sends event emails
(orders opened or closed, order status) every `Notifications__DispatchIntervalSeconds`, and a
scheduler works out the license digest, the planned close reminders and the milestone reminders every
15 minutes from 08:00 Europe/Madrid. Locally they land in Mailpit (<http://localhost:8025>). To send
today's scheduled notifications at once, whatever the hour:

```bash
docker compose run --rm api send-notifications
dotnet run --project backend/src/PolvorApp.Api send-notifications
```

It sends nothing twice and exits with 1 when a delivery failed. `Notifications__Enabled=false` turns
both background services off (the command still works); test hosts do so and call the runs directly.

### Audit retention

The API keeps audit entries for a limited time (`add-audit-privacy`, SEC-05): access and security
events (sign-ins, failed sign-ins, lockouts, recovery-code use, password reset requests, lender and
personal-data lookups) for `Audit__SecurityRetentionDays` (365), everything else for
`Audit__RetentionYears` (5). A background purge runs a minute after start-up and then daily, deletes
in batches and records one `AuditEntriesPurged` entry with the counts. `purge-audit` runs it at once;
`Audit__PurgeEnabled=false` turns the background purge off (test hosts do so).

A database trigger refuses every `UPDATE`, `DELETE` and `TRUNCATE` on `audit.audit_entries`, also with
`session_replication_role = replica`. Only the audit module's `AuditMaintenance` may change entries:
deletes inside a transaction marked for the purge, and changes of `data` alone inside one marked for a
GDPR redaction. So do not try to "clean up" audit rows by hand in a local database — drop the database
or re-create the compose volume instead.

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
RATE_LIMIT_AUTH_PER_MINUTE=300 RATE_LIMIT_AUTH_EMAIL_PER_15_MINUTES=100 RATE_LIMIT_PERSONAL_DATA_WRITES_PER_MINUTE=600 RATE_LIMIT_IMAGE_UPLOADS_PER_MINUTE=200 RATE_LIMIT_SPREADSHEET_IMPORTS_PER_MINUTE=100 RATE_LIMIT_EXPORTS_PER_MINUTE=300 RATE_LIMIT_PRIVACY_PER_MINUTE=100 RATE_LIMIT_INSIGHTS_READS_PER_MINUTE=600 RATE_LIMIT_HANDOVER_SYNC_PER_MINUTE=600 NOTIFICATIONS_DISPATCH_INTERVAL_SECONDS=2 docker compose up -d --build --wait
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

Locally the suite runs on 4 workers, whatever the machine's cores: the one compose stack is the
limit, and more workers make pages (Firefox first) time out before they settle. Set `E2E_WORKERS`
to change it. The notifications journey starts `docker compose run ... send-notifications` from
Node, so `docker` must be on the `PATH` that Node sees (PowerShell and a normal terminal have it).

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
run side by side; the E2E job builds its images while it installs the browsers. The backend's tests
run as three shards at once (`backend-tests`); the required `backend` check passes only when they
and `backend-checks` (format, build, contract drift) pass and their merged coverage holds. On `main`,
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
