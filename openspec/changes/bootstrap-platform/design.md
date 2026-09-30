# Design

## Context

The repository contains only discovery docs, OpenSpec config and the curated ECC toolkit; there is
no code, no `.github/` and no build tooling. The stack and topology are fixed by ADR-0001, 0002,
0006, 0007, 0010 and 0011; ADR-0009 (design system) is deliberately left to change #2. See
`proposal.md` for motivation and scope and `specs/platform/spec.md` for the required behaviour.

Constraints that shape the approach: a single maintainer (NFR-09) — few moving parts, no library
that overlaps with an ADR choice; a public repository (NFR-14, SEC-11) — nothing secret or real in
git; three locales from day one (NFR-02); later offline support (NFR-03) must not need a rewrite.

## Goals / Non-Goals

**Goals:**
- A layout and conventions that every later change copies without re-deciding them (module
  shape, feature folders, test projects, i18n namespaces, contract generation).
- Every quality gate that later changes rely on is live and green from the first commit.
- The whole stack runs locally with one command and in CI the same way.

**Non-Goals (design level):**
- No persistence abstractions beyond a database connection for the readiness check; the first
  module DbContext, migrations runner and audit hooks are built in #3 following the conventions
  below.
- No styling system: plain CSS for the shell, replaced by tokens in #2.
- No deployment or runtime secrets management beyond environment variables.

## Decisions

### D1. Repository layout

```
backend/
  PolvorApp.slnx                     global.json, Directory.Build.props, Directory.Packages.props
  src/PolvorApp.Api/                 host = composition root + platform capability
  src/PolvorApp.SharedKernel/        module contract, seeding contract, shared primitives
  src/Modules/                       one folder per capability (empty in this change)
  tests/PolvorApp.Api.Tests/         integration tests (WebApplicationFactory + Testcontainers)
  tests/PolvorApp.ArchitectureTests/ module-boundary rules
contracts/openapi.json               committed API description (source for the client)
frontend/                            Vite app, unit tests, e2e/ (Playwright), nginx/, Dockerfile
compose.yaml, .env.example           local environment
.github/workflows/ci.yml, codeql.yml, .github/dependabot.yml
LICENSE, README.md, .editorconfig    repository hygiene
docs/development.md, docs/third-party-licenses.md
```

The `platform` capability lives in the API host (`PolvorApp.Api/Platform/`) and in
`frontend/src/app/` + `frontend/src/features/platform/`, because it is the composition root rather
than a domain module. *Alternative:* a `Modules/Platform` project — rejected, it would only hold
host concerns and invert the dependency (host → module → host services).

### D2. Module shape and boundary enforcement (ADR-0001)

Each future capability gets two projects: `Modules/<Name>/PolvorApp.<Name>` (implementation, types
`internal` by default) and `Modules/<Name>/PolvorApp.<Name>.Contracts` (public contract: DTOs,
service interfaces, integration events). Rules, checked by `PolvorApp.ArchitectureTests`:

1. A module project may reference only `SharedKernel` and other modules' `.Contracts` projects.
2. A `.Contracts` project may reference only `SharedKernel`.
3. `SharedKernel` references no module and not the API host.

Implementation: the rules are a pure function over an assembly-reference graph (name → referenced
names), unit-tested with synthetic graphs (so the failing scenario is proven today, while no module
exists) and applied to the real graph built by reflection over `PolvorApp.*` assemblies loaded from
the API host's output. Combined with `internal`-by-default and project references, this enforces the
spec without a new library. *Alternatives:* ArchUnitNET / NetArchTest — richer type-level rules but
another dependency; revisit if type-level rules become necessary.

Module registration: `SharedKernel` defines `IModule` (`AddServices(IServiceCollection,
IConfiguration)`, `MapEndpoints(IEndpointRouteBuilder)`); the host lists modules explicitly in
`Program.cs` (no assembly scanning — explicit, trimming-friendly, easy to read).

Persistence conventions (recorded now, implemented from #3): one DbContext per module, one
PostgreSQL schema per module with its own migrations-history table, migrations applied by an
explicit `migrate` host command (not on web startup).

### D3. Runtime versions

Verified against the registries on 2026-09-30 (task 1.1):

| Component | Pinned | Notes |
|---|---|---|
| .NET | SDK `10.0.100` feature band via `global.json` (`rollForward: latestFeature`); images `mcr.microsoft.com/dotnet/sdk:10.0-noble`, `aspnet:10.0-noble-chiseled-extra` | .NET 10 is the current LTS (runtime 10.0.12) |
| Node | `24` (`.nvmrc`, `engines: >=24`) | Active LTS |
| PostgreSQL | `postgres:18.6-alpine` | 19 is still beta |
| nginx | `nginxinc/nginx-unprivileged:1.30-alpine` | stable line (1.30.5) |
| MinIO | `cgr.dev/chainguard/minio` pinned by digest | see Risks |
| Mailpit | `axllent/mailpit:v1.31.3` | |

Images in `compose.yaml` are pinned by tag plus digest; Dependabot (`docker`) proposes updates.
npm is the package manager (fewest moving parts). Backend uses central package management,
`Nullable`, `TreatWarningsAsErrors`, latest analyzers and NuGet audit at `high` so vulnerable
packages break restore.

### D4. Platform endpoints (API)

| Method | Path | Response | Notes |
|---|---|---|---|
| GET | `/api/health/live` | `200 {status}` | no checks |
| GET | `/api/health/ready` | `200/503 {status, checks:[{name,status}]}` | `database` check |
| GET | `/api/system/info` | `200 {version, commit}` | from assembly informational version |
| GET | `/api/openapi/v1.json` | OpenAPI document | Development only |

- Readiness uses a small custom health check that opens a connection from an `NpgsqlDataSource`
  (only `Npgsql` is added now; EF Core arrives in #3). A custom JSON writer emits names and statuses
  only. *Alternative:* `AspNetCore.HealthChecks.NpgSql` — one more dependency for ~15 lines.
- Version/commit: `Directory.Build.props` defaults `Version` to `0.1.0`; CI and the Dockerfile pass
  `-p:Version=$APP_VERSION -p:SourceRevisionId=$GIT_COMMIT`.
- All endpoints are anonymous and personal-data-free (spec "Anonymous surface"). From #3, the
  default is authenticated: endpoints must opt out explicitly.

### D5. Cross-cutting API behaviour

- **Errors**: `AddProblemDetails` with a customisation that adds `traceId` and sets a localised
  `title` per status code; `UseExceptionHandler` + `UseStatusCodePages` so unmatched routes and
  exceptions produce problem details. Exception details only in Development.
- **Logging/correlation**: built-in logging with the JSON console formatter, scopes enabled and
  `ActivityTrackingOptions.TraceId`. The framework's hosting logger is disabled because its request
  scope carries the raw path (possible personal data); an `ActivityListener` on the
  `Microsoft.AspNetCore` source keeps request activities, and so trace ids, available. A request-logging middleware logs method, route template (or
  path), status and elapsed time — never query string, headers or bodies. The W3C trace id is
  returned in an `X-Trace-Id` header and as `traceId` in problem details. *Alternative:* Serilog —
  not needed at this scale; revisit with an error-tracking service before go-live (NFR-12).
- **Configuration**: `ConnectionStrings__Postgres` and other settings bound to options classes with
  data-annotation validation and `ValidateOnStart`; a startup failure logs the key name only. The
  build-time OpenAPI generator runs the entry point, so validation is skipped when the entry
  assembly is the document-generation tool (see Risks).
- **Localisation**: `RequestLocalizationOptions` with supported cultures `es-ES` (default),
  `ca-ES-valencia`, `en` and a small `Accept-Language` provider that maps by primary subtag
  (`es*` → `es-ES`, `ca*` → `ca-ES-valencia`, `en*` → `en`) in quality order, like the UI (D8). Texts in `.resx`
  resources (`Platform/Localization/SharedResources*.resx`), starting with problem-details titles for
  400, 404, 405, 500 and 503. Valencian texts are stored as `SharedResources.ca.resx`: MSBuild does
  not build or copy satellite assemblies for the `ca-ES-valencia` variant name, and the
  `ca-ES-valencia` culture falls back to its parent `ca`, the only Catalan variant offered.
- **Headers and CORS**: no CORS policy is registered (same origin, ADR-0011). The API adds
  `X-Content-Type-Options`, `Referrer-Policy` and `Content-Security-Policy: default-src 'none';
  frame-ancestors 'none'` to its own responses (defence in depth); nginx adds the UI policy (D8).

### D6. Synthetic seed runner (SEC-11)

`SharedKernel` defines `IDataSeeder` (`Order`, `SeedAsync`) and a fixed `SyntheticData.RandomSeed`
constant that module seeders must use with their fakers (Bogus is added by the first module that
needs it). The host recognises the verb `seed` (`dotnet PolvorApp.Api.dll seed`, or
`docker compose run --rm api seed`): it builds the host without starting Kestrel, refuses with exit
code 1 unless the environment is `Development`, `Staging` or `Testing` (allow-list, so `Production`
and misspelt names are refused), validates the database options, then runs registered seeders in
order inside a scope and exits 0; any failure or cancellation is logged and exits 1. Seeding is not
transactional: an aborted run leaves partial synthetic data (reset the database and rerun). Tests register a synthetic seeder to prove ordering, the Production guard and
determinism. The `migrate` verb from D2 is added to the same dispatcher in #3.

### D7. Frontend structure (ADR-0011)

```
src/main.tsx
src/app/            providers (QueryClient, i18n), router, shell/ (AppShell, VersionFooter, shell.css)
src/features/platform/pages/   HomePage, NotFoundPage
src/components/app/LanguageSwitcher.tsx   (composite named in ADR-0009; restyled in #2)
src/components/ui/  empty until #2
src/i18n/           config (supported languages, detection mapping), index, locales/<lng>/common.json
src/api/http.ts     fetch mutator; src/api/generated/ (orval output, git-ignored)
src/lib/format.ts   Intl-based date/number formatters for the active language
```

- TypeScript `strict` + `noUncheckedIndexedAccess`; path alias `@/`.
- Routing: React Router data router; routes `/` (HomePage) and `*` (NotFoundPage) under `AppShell`.
- Shell: skip-to-content link, `<header>` (app name, `LanguageSwitcher` as a labelled native
  `<select>`), `<main id="main">`, `<footer>` (version via the generated `useGetSystemInfo` hook;
  translated fallback on error). Plain CSS with visible `:focus-visible` outline, fluid down to 360 px.
- Server state: TanStack Query with conservative defaults (`retry: 1`, no refetch on focus).
- API client: orval (`client: react-query`, `httpClient: fetch`) from `../contracts/openapi.json`
  into `src/api/generated`; the mutator `http.ts` sets `credentials: 'same-origin'` and
  `Accept-Language` from `<html lang>` (which i18n keeps equal to the active language, so `api/`
  does not depend on `i18n/`), and turns unusable responses into a typed `ApiProblemError`. `generate:api` runs before `dev`, `build`, `typecheck` and `test`. Generated code is not
  committed (the committed contract is the reviewable artefact).
- PWA: `vite-plugin-pwa` `generateSW`, `registerType: 'autoUpdate'`, register script injected as an
  external file (CSP, no inline), precache static assets only, `navigateFallbackDenylist: [/^\/api\//]`,
  no runtime caching. Manifest: name "PolvorApp", placeholder neutral theme colour and generated
  placeholder icons (brand in #2).

### D8. i18n (UC-27, ADR-0007)

- i18next + react-i18next + `i18next-browser-languagedetector`; `supportedLngs: ['es-ES',
  'ca-ES-valencia', 'en']`, `fallbackLng: 'es-ES'`, `load: 'currentOnly'`, detection order
  `localStorage` → `navigator`, cached in `localStorage` (key `polvorapp.language`).
- Browser tags are mapped by primary subtag before matching: `es*` → `es-ES`, `ca*` → `ca-ES-valencia`,
  `en*` → `en`; anything else falls back to `es-ES`. (A browser set to Catalan gets Valencian —
  the only Catalan variant offered.)
- On `languageChanged`, set `document.documentElement.lang`. `index.html` ships `lang="es-ES"`.
- Resources: one JSON file per locale and namespace, bundled statically for `common`; later
  features add their own namespace (lazy loading can be introduced when bundle size warrants).
- Guardrails: `eslint-plugin-i18next` `no-literal-string` (JSX mode; the plugin pins ESLint 9, and
  typescript-eslint pins TypeScript 6.0) and `scripts/check-i18n.mjs`
  (no dependency) that flattens every namespace per locale and fails on missing/extra/empty keys.
- Formatting: `src/lib/format.ts` wraps `Intl.DateTimeFormat`/`Intl.NumberFormat` with the active
  language; date-fns is added when a feature needs date arithmetic.

Initial keys (`common` namespace; language names are autonyms, identical in every locale):

| Key | es-ES |
|---|---|
| `app.name` | PolvorApp |
| `shell.skipToContent` | Saltar al contenido |
| `shell.language.label` | Idioma |
| `shell.language.options.es-ES` / `ca-ES-valencia` / `en` | Español / Valencià / English |
| `shell.footer.version` | Versión {{version}} |
| `shell.footer.versionUnavailable` | Versión no disponible |
| `home.title` | Bienvenida |
| `home.description` | Portal de gestión de arcabuceros de la Federación. |
| `notFound.title` | Página no encontrada |
| `notFound.description` | La página que buscas no existe. |
| `notFound.backHome` | Volver al inicio |

Valencian and English values are written in the change; Valencian is flagged for review by a
speaker familiar with festival vocabulary (ADR-0007 consequence).

### D9. Containers and local environment (ADR-0006)

- `backend/Dockerfile`: SDK build stage → `mcr.microsoft.com/dotnet/aspnet:<lts>-noble-chiseled-extra`
  runtime (non-root, includes ICU — required for `ca-ES-valencia`; plain chiseled images run in
  invariant-globalisation mode). Build args `APP_VERSION`, `GIT_COMMIT`.
- `frontend/Dockerfile`: Node build stage → `nginxinc/nginx-unprivileged` serving `dist/`, with
  `nginx/default.conf`: SPA fallback to `index.html`, `location /api/` proxied to `api:8080`,
  `Cache-Control: no-cache` for `index.html`/`sw.js`/manifest, immutable caching for hashed assets,
  and the security headers: `Content-Security-Policy: default-src 'self'; script-src 'self';
  style-src 'self'; img-src 'self' data: blob:; connect-src 'self'; worker-src 'self';
  manifest-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self';
  object-src 'none'`, `X-Content-Type-Options`, `Referrer-Policy: no-referrer`,
  `Permissions-Policy: camera=(self), geolocation=(), microphone=()`. HSTS is left to the
  production TLS proxy.
- `compose.yaml`: `db` (PostgreSQL, `pg_isready` healthcheck, named volume), `storage` (MinIO, no
  healthcheck: the image has no shell or client), `mail` (Mailpit), `api` (depends on healthy `db`;
  no healthcheck: chiseled image), `web` (host port `8080`; healthcheck through nginx to
  `/api/health/ready`, so `docker compose up --wait` means web, API and database are ready). Host ports bind to `127.0.0.1` only. Values come from `.env` (copied from `.env.example`,
  placeholders such as `local-only-change-me`). `.env` is git-ignored.
- Two dev loops documented in `docs/development.md`: full stack (`docker compose up --build`) or
  infra only (`docker compose up db storage mail`) + `dotnet watch` + `npm run dev` with the Vite
  proxy `/api` → `http://localhost:5080`.

### D10. Testing strategy

- **Backend** (xUnit, `PolvorApp.Api.Tests`): `WebApplicationFactory<Program>` with a PostgreSQL
  Testcontainer shared per collection; tests for each spec scenario (health healthy/unhealthy via an
  unreachable connection string, system info fields, 404 problem details, localised titles,
  Production vs Development exception output, trace-id header, no query string in logs via a
  capturing logger provider, missing config exits non-zero, security headers, no CORS headers, seed
  guard/order/determinism). Coverage via coverlet, gate 80 % lines.
- **Architecture** (`PolvorApp.ArchitectureTests`): rule function against synthetic graphs + real graph.
- **Frontend** (Vitest + Testing Library + `axe-core` through a small `axeViolations` helper; the
  `vitest-axe` wrapper is unmaintained): shell renders, version footer success and
  error states (generated client mocked at the fetch layer with MSW, already implied by the
  react-testing guidance — added as a dev dependency), language switch updates text and `lang`,
  detection mapping table, persistence, `Accept-Language` in the mutator, not-found page, axe on
  shell and not-found. Coverage gate 80 % lines on `src/` excluding generated code.
- **E2E** (Playwright + `@axe-core/playwright`, `frontend/e2e/`) against the compose stack at
  `http://localhost:8080`, projects Chromium desktop and a 360 px mobile viewport: shell + version,
  switch to Valencian and reload, not-found, security headers, axe scan, `/api` response not served
  by the service worker (`response.fromServiceWorker()`).
- All fixtures synthetic; no data from `docs/sources/`.

### D11. CI (GitHub Actions) and supply chain

`ci.yml` on `pull_request` and `push` to `main`, actions pinned by commit SHA (Dependabot keeps them
current), least-privilege `permissions`, `persist-credentials: false` and a timeout on every job:

| Job | Steps |
|---|---|
| `backend` | setup-dotnet from `global.json`, restore (NuGet audit fails on high/critical), `dotnet format --verify-no-changes`, build Release, test + coverage gate, `git diff --exit-code contracts/` (contract drift) |
| `frontend` | setup-node from `.nvmrc`, `npm ci`, `npm audit --omit=dev --audit-level=high`, lint, typecheck, `check-i18n`, test + coverage gate, build |
| `e2e` | needs both; copy `.env.example`, `docker compose up -d --build --wait`, Playwright, upload report on failure |
| `images` | build `api` and `web` images with Buildx + GHA cache on every run, no registry access |
| `publish-images` | `main` only, after `images`, `e2e` and `secrets` pass: push to GHCR (`ghcr.io/<owner>/polvorapp-api`, `-web`, tags `sha-<sha>` and `main`); the only job with `packages: write`; packages are **public**, matching the public MIT repo, so hosts pull without registry credentials (images never contain secrets) |
| `secrets` | gitleaks CLI over the full history of the PR/push |
| `dependency-review` | `actions/dependency-review-action`, `fail-on-severity: high` (PRs only) |

`codeql.yml`: C# and JavaScript/TypeScript, on PR, push to `main` and weekly. `dependabot.yml`:
`nuget`, `npm`, `github-actions`, `docker`, weekly, grouped minor/patch. Repository settings that CI
cannot enforce (branch protection with required checks, secret-scanning push protection) are
documented in `docs/development.md` for the maintainer to enable.

### D12. Licensing (ADR-0008, ADR-0010)

`LICENSE` (MIT, `Copyright (c) 2026 Jorge Torregrosa Lloret`). `docs/third-party-licenses.md` lists each direct dependency with its license and,
for non-permissive ones, the justification. QuestPDF's Community license (free for non-profits and
organisations under the revenue threshold) is checked against the current license text and recorded
there now, although the package is added only in #12/#13; MinIO (AGPL, local development only, not
distributed) is recorded too.

### Security, GDPR and audit

- No personal data is stored, processed or logged by this change; no DPIA impact.
- SEC-11: seed Production guard, synthetic-only fixtures, gitleaks + push protection, `.env` ignored.
- NFR-12: logs exclude query strings, headers and bodies; errors hide internals outside Development.
- SEC-01: TLS is provided by the production reverse proxy (out of scope); the app is same-origin
  with no CORS, strict CSP and no framing.
- Containers run as non-root; local ports bound to loopback.
- Audit logging (SEC-05): nothing to audit (no writes, no exports). The audit hook is designed in #3.

### Data model / migrations

None. Conventions for future modules are fixed in D2.

## Risks / Trade-offs

- [MinIO no longer publishes community images (`minio/minio` is gone from Docker Hub and Quay,
  verified 2026-09-30)] → use Chainguard's free, source-built `cgr.dev/chainguard/minio` (still
  MinIO, AGPL, local development only) pinned by digest. Chainguard's free tier only offers
  `latest`, so updates are digest bumps. If it disappears, propose an ADR replacing the local S3
  emulator (e.g. Garage, SeaweedFS, RustFS) before #6; the S3 API keeps the app unaffected.
- [Build-time OpenAPI generation executes `Program` and would trip fail-fast config validation]
  → skip validation and external connections when running under the document generator; covered by
  the backend CI job building the contract.
- [`ca-ES-valencia` support differs across runtimes (ICU in containers, i18next fallback rules)]
  → `-extra` runtime image with ICU; `load: 'currentOnly'`; explicit tests for the culture on both
  sides.
- [Strict CSP may break a future library (inline styles/scripts)] → E2E asserts no CSP violations
  in the console; any relaxation is a reviewed change to `nginx/default.conf`.
- [Testcontainers needs Docker on the developer machine (Windows)] → documented; the same stack is
  already required for compose.
- [E2E against the full compose stack slows CI] → single job, image layer caching, only after unit
  jobs pass.
- [`autoUpdate` activates a new service worker immediately; once forms or lazy routes exist an open
  tab could lose input or request stale chunks] → switch to `registerType: 'prompt'` (update
  banner) in the first change that adds forms or lazy routes.
- [Plain-CSS shell is throwaway] → deliberately minimal; #2 replaces it.

## Migration Plan

Greenfield: no data, no deployment. Rollback is reverting the change's commits. After merge the
maintainer enables branch protection (required checks: `backend`, `frontend`, `e2e`, `secrets`) and
secret-scanning push protection, as documented.
