# Tasks

## 1. Repository foundations

- [x] 1.1 Verify current .NET LTS, Node LTS, PostgreSQL, nginx and MinIO image availability with Context7/registries, record the pinned versions in design.md D3 and the MinIO outcome (pinned digest or ADR needed) in the Risks section; verify by the updated design.md
- [x] 1.2 Add MIT `LICENSE` (`Copyright (c) 2026 Jorge Torregrosa Lloret`), `.editorconfig`, and `.gitignore` entries (`.env`, `bin/`, `obj/`, `node_modules/`, `dist/`, `coverage/`, `frontend/src/api/generated/`, Playwright reports); verify `git status` shows no build output after a build
- [x] 1.3 Create `docs/third-party-licenses.md` with the license register format, QuestPDF Community license check against the current license text, and MinIO (AGPL, local dev only); verify each entry links to its license source
- [x] 1.4 Review group 1 with `code-reviewer` (read-only) and fix CRITICAL/HIGH findings

## 2. Backend skeleton and module boundaries

- [x] 2.1 Create `backend/` with `global.json`, `Directory.Build.props` (nullable, warnings as errors, analyzers, default `Version` 0.1.0, NuGet audit level high), `Directory.Packages.props`, `PolvorApp.slnx`, projects `PolvorApp.Api`, `PolvorApp.SharedKernel`, `PolvorApp.Api.Tests`, `PolvorApp.ArchitectureTests`, and `src/Modules/README.md` describing the module shape (design D2); verify `dotnet build -c Release` succeeds with zero warnings
- [x] 2.2 Write architecture-rule tests against synthetic reference graphs (module→other module fails naming both, module→Contracts passes, Contracts→module fails, SharedKernel→module/host fails), then implement the rule function; verify tests go red then green
- [x] 2.3 Apply the rules to the real `PolvorApp.*` assembly graph and add `IModule` to SharedKernel with explicit registration in `Program.cs`; verify the architecture test suite passes
- [x] 2.4 Review group 2 in parallel with `csharp-reviewer` and `code-reviewer`; fix CRITICAL/HIGH findings

## 3. API platform behaviour

- [x] 3.1 Add the shared integration-test fixture (`WebApplicationFactory<Program>` + PostgreSQL Testcontainer, capturing logger provider); verify a smoke test starts the host
- [x] 3.2 Write tests for configuration validation (missing `ConnectionStrings__Postgres` exits non-zero and logs the key name but no value), then implement options binding with `ValidateOnStart` and the document-generator bypass; verify tests pass
- [x] 3.3 Write tests for `/api/health/live` and `/api/health/ready` (healthy, database unreachable → 503 with `database` check and no connection details, liveness ignores the database), then implement the Npgsql check and JSON writer; verify tests pass
- [x] 3.4 Write tests for `/api/system/info` (only `version` and `commit`), then implement it from the assembly informational version; verify tests pass
- [x] 3.5 Write tests for problem details (unknown `/api/...` route → 404 problem with `traceId`; unhandled exception hides details in Production and shows them in Development via a test-only endpoint), then implement exception handling and status-code pages; verify tests pass
- [x] 3.6 Write tests for request culture (`ca-ES-valencia` and `en` titles, `de-DE` → Spanish), then add request localisation and `SharedResources` `.resx` titles for 400/404/405/500/503 in es-ES, ca-ES-valencia and en; verify tests pass
- [x] 3.7 Write tests for `X-Trace-Id` header matching the logged trace id and for query-string values absent from logs, then implement the JSON console logging configuration and request-logging middleware; verify tests pass
- [x] 3.8 Write tests for API security headers and absence of `Access-Control-Allow-Origin` on cross-origin requests, then implement the header middleware; verify tests pass
- [x] 3.9 Write tests for the `seed` verb (Production refusal with exit code 1 and no writes, ordered execution of registered seeders, identical output on two empty databases using `SyntheticData.RandomSeed`), then implement `IDataSeeder`, `SyntheticData` and the verb dispatcher; verify tests pass
- [x] 3.10 Configure build-time OpenAPI generation into `contracts/openapi.json` (Development-only `/api/openapi/v1.json` endpoint) and commit the document; verify a clean rebuild leaves `git diff --exit-code contracts/` empty
- [x] 3.11 Enable coverlet with an 80 % line gate; verify `dotnet test` reports coverage and passes the gate
- [x] 3.12 Review group 3 in parallel with `csharp-reviewer`, `security-reviewer` and `silent-failure-hunter`; fix CRITICAL/HIGH findings

## 4. Frontend skeleton and generated client

- [x] 4.1 Scaffold `frontend/` with Vite + React + TypeScript (strict, `noUncheckedIndexedAccess`, `@/` alias), `.nvmrc`, `engines`, folder layout from design D7 (`app/`, `features/platform/`, `components/{ui,app}/`, `i18n/`, `api/`, `lib/`); verify `npm run build` and `npm run typecheck` succeed
- [x] 4.2 Configure ESLint (typescript-eslint, react-hooks, jsx-a11y, `eslint-plugin-i18next` no-literal-string, ignore generated code) and Prettier; verify `npm run lint` passes and fails on a temporary literal JSX string
- [x] 4.3 Configure Vitest + Testing Library + `axe-core` helper + MSW with an 80 % line coverage gate excluding generated code; verify a placeholder test runs under `npm test -- --coverage`
- [x] 4.4 Configure orval from `../contracts/openapi.json` into `src/api/generated` and wire `generate:api` before `dev`, `build`, `typecheck` and `test`; verify the generated `useGetSystemInfo` hook type-checks
- [x] 4.5 Write tests for the fetch mutator (`Accept-Language` from the active language, same-origin credentials, problem-details error mapping), then implement `src/api/http.ts`; verify tests pass
- [x] 4.6 Review group 4 in parallel with `typescript-reviewer` and `code-reviewer`; fix CRITICAL/HIGH findings

## 5. i18n and language switching (UC-27)

- [x] 5.1 Write tests for `scripts/check-i18n.mjs` (missing key, extra key, empty value each fail naming locale and key; complete set passes), then implement it and the `check-i18n` npm script; verify tests pass
- [x] 5.2 Write tests for language detection mapping (`ca`/`ca-ES` → `ca-ES-valencia`, `es-MX` → `es-ES`, `en-US` → `en`, `fr-FR` → `es-ES`) and persistence in `localStorage`, then implement `src/i18n/` config; verify tests pass
- [x] 5.3 Add `common` namespace resources for es-ES, ca-ES-valencia and en with the keys from design D8; verify `npm run check-i18n` passes
- [x] 5.4 Write tests for `LanguageSwitcher` (autonym options, switching updates visible text without reload, `document.documentElement.lang` follows, axe clean), then implement it in `src/components/app/`; verify tests pass
- [x] 5.5 Write tests for `src/lib/format.ts` date and number formatting per active language, then implement it; verify tests pass
- [x] 5.6 Review group 5 in parallel with `react-reviewer` and `a11y-architect`; fix CRITICAL/HIGH findings; flag Valencian texts for native review in the PR description

## 6. Application shell and PWA

- [x] 6.1 Write tests for `AppShell` (skip link, header with app name and switcher, main landmark, axe clean), then implement it with minimal plain CSS (visible focus, fluid to 360 px) and the router with `/` → `HomePage`; verify tests pass
- [x] 6.2 Write tests for `VersionFooter` (shows translated version from a mocked system-info response; shows "version unavailable" on error), then implement it with the generated hook; verify tests pass
- [x] 6.3 Write tests for `NotFoundPage` (translated title, link back home, axe clean) under the `*` route, then implement it; verify tests pass
- [x] 6.4 Configure `vite-plugin-pwa` (generateSW, auto-update, external register script, `/api` excluded from navigation fallback, no runtime caching) with manifest and generated placeholder icons, plus Vite dev proxy `/api` → API; verify `npm run build` emits `sw.js` and `manifest.webmanifest` and the SW contains no `/api` caching route
- [x] 6.5 Review group 6 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`; fix CRITICAL/HIGH findings

## 7. Containers and local environment

- [x] 7.1 Write `backend/Dockerfile` (SDK build → chiseled-extra runtime, non-root, `APP_VERSION`/`GIT_COMMIT` args); verify `docker build` succeeds and `docker run ... seed` with `ASPNETCORE_ENVIRONMENT=Production` exits 1
- [x] 7.2 Write `frontend/Dockerfile` and `frontend/nginx/default.conf` (SPA fallback, `/api/` proxy, cache rules, CSP and security headers from design D9); verify `docker build` succeeds
- [x] 7.3 Write `compose.yaml` (`db`, `storage`, `mail`, `api`, `web`, healthchecks, loopback-only ports, named volumes) and `.env.example` with placeholders only; verify `docker compose up -d --build --wait` from a copied `.env.example` yields `Healthy` at `http://localhost:8080/api/health/ready`
- [x] 7.4 Write `README.md` (purpose, quick start, links) and `docs/development.md` (prerequisites, both dev loops, seed command, contract regeneration, i18n workflow, repository settings to enable: branch protection, secret push protection and public visibility of the GHCR packages); verify each documented command runs as written on a fresh clone
- [x] 7.5 Review group 7 in parallel with `security-reviewer` and `code-reviewer`; fix CRITICAL/HIGH findings

## 8. End-to-end tests

- [x] 8.1 Configure Playwright in `frontend/e2e/` (base URL `http://localhost:8080`, Chromium desktop and 360 px mobile projects, `@axe-core/playwright`, fail on console CSP violations); verify `npx playwright test --list` shows both projects
- [x] 8.2 Write E2E tests: shell with API version, switch to Valencian then reload keeps Valencian and `lang="ca-ES-valencia"`, not-found page, security headers on UI and `/api`, axe scans of shell and not-found, `/api/system/info` not served from the service worker; verify they pass against the compose stack
- [x] 8.3 Review group 8 with `e2e-runner` (Playwright only, per ADR-0011) and `pr-test-analyzer`; fix CRITICAL/HIGH findings

## 9. Continuous integration and supply chain

- [ ] 9.1 Write `.github/workflows/ci.yml` jobs `backend`, `frontend`, `e2e`, `images` (GHCR push on `main` only), `secrets` (gitleaks CLI) and `dependency-review` with SHA-pinned actions and least-privilege permissions (design D11); verify with `actionlint` and a green run on a pull request
- [ ] 9.2 Write `.github/workflows/codeql.yml` (C#, JavaScript/TypeScript) and `.github/dependabot.yml` (nuget, npm, github-actions, docker; weekly, grouped); verify CodeQL completes on the pull request
- [ ] 9.3 Prove the gates fail: on a throwaway branch push a synthetic fake secret, a missing ca-ES-valencia key and an un-regenerated contract change; verify the `secrets`, `frontend` and `backend` jobs fail respectively, then delete the branch
- [x] 9.4 Update `docs/README.md` index (development guide, third-party licenses) and `docs/mvp.md` status of change #1; verify links resolve
- [x] 9.5 Review group 9 in parallel with `security-reviewer` and `code-reviewer`; fix CRITICAL/HIGH findings

## 10. Verification

- [ ] 10.1 Run `verification-loop` (build, types, lint, tests with coverage ≥ 80 %, i18n check, contract check, security grep for secrets and personal data, diff review) and the Playwright suite on the compose stack; verify a PASS report and a green CI run on the pull request
- [ ] 10.2 Check every scenario in `specs/platform/spec.md` maps to at least one passing test or CI check (`pr-test-analyzer`); verify no scenario is uncovered
