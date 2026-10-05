# Tasks

## 1. Research

- [x] 1.1 Check with Context7:
  - MimeKit `MailboxAddress` display-name encoding and `ReplyTo`;
  - EF Core 10 `xmin` concurrency on a singleton row and check constraints in migrations.

  Check with `gh search code` for settings pages built on react-hook-form with per-section saves.
  Record findings in design.md. Verify: design.md is updated, with no new open question.

## 2. Backend: settings and API

- [x] 2.1 Write Testcontainers tests for the migration:
  - the singleton row has today's names, "Unión de Comparsas", "PolvorApp", 7 and 7;
  - the check constraints refuse 0 and 15 days.

  Then add the columns and the migration (D1). Verify: the tests pass, and
  `has-pending-model-changes` reports none.
- [x] 2.2 Write integration tests for `GET /federation-settings` and the four `PUT`s (D2):
  - Admin only, `403` for FiringChiefs;
  - every validation reason, including a CR/LF in the sender name and a non-https website;
  - `409` on a stale version;
  - one audit entry per save with previous and new values;
  - `GET /federation` carries the identity for every signed-in user.

  Then implement them. Verify: the tests pass and the OpenAPI document lists the endpoints.
- [x] 2.3 Write tests for `IFederationSettings` (a snapshot, memoised per request), then implement
  and register it (D3). Verify: the tests and architecture tests pass.
- [x] 2.4 Review group 2 in parallel with `csharp-reviewer`, `database-reviewer` and
  `security-reviewer`. Fix CRITICAL/HIGH findings.

## 3. Backend: readers

- [x] 3.1 Update the `Badges` and `Distribution` golden-file and text tests so the official names
  come from the settings. A changed Valencian name must print. Then pass the names from the
  settings and delete `FederationNames`. Verify: the golden files are unchanged with the default
  settings and the new tests pass.
- [x] 3.2 Update the `Schedule` unit tests for parameterised lead times (2..N close window, 0..N
  milestones), with defaults unchanged. Add notification integration tests for a 10-day close
  lead and a 3-day milestone lead. Then implement them. Verify: the tests pass.
- [x] 3.3 Write email sender tests:
  - the From display name and address;
  - the reply-to only when set;
  - the footer with short name and contact, in three languages.

  Then implement them (D4). Verify: the tests pass, and Mailpit shows the headers in the local
  stack.
- [x] 3.4 Review group 3 in parallel with `csharp-reviewer` and `silent-failure-hunter`. Fix
  CRITICAL/HIGH findings.

## 4. Frontend

- [x] 4.1 Regenerate the API client. Write `SettingsPage` tests:
  - the five sections with their values and "used in" sentences;
  - editing each section in its panel, with validation, error summary, save notice and `409`
    handling;
  - the logo picture actions;
  - FiringChief not allowed;
  - axe;
  - 360 px.

  Then implement the page and route (D5). Verify: the tests pass.
- [x] 4.2 Add the Settings navigation entry and remove the logo section from `ComparsasPage`,
  updating its tests. Verify: the AppShell and ComparsasPage tests pass.
- [x] 4.3 Add the `catalog:settings.*`, `ui:nav.settings` and notification footer texts in es-ES,
  ca-ES-valencia and en. Verify: `npm run check-i18n` passes.
- [x] 4.4 Review group 4 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH
  findings.

## 5. Docs and verification

- [x] 5.1 Update the docs:
  - `docs/development.md`: settings vs environment variables;
  - `docs/compliance.md`: no personal data or secrets in settings;
  - `docs/data-model.md`: `FederationSettings`;
  - `docs/design/patterns.md`: the settings page;
  - `.env.example` comments for `EMAIL_FROM`.

  Verify: the docs match the screens and variables.
- [x] 5.2 Add Playwright specs on the seeded stack:
  - an Admin changes the sender name and the milestone lead time, then sees them after reload;
  - the Federation logo is managed from Settings;
  - a FiringChief gets "not allowed";
  - axe in both themes.

  Verify: the specs pass, and the suite passes twice in a row.
- [x] 5.3 Run `verification-loop`:
  - build, types and lint;
  - backend and frontend tests with at least 80 % coverage on the settings module code, the
    readers and the page;
  - a security grep: no secrets, header-injection guard, Admin-only writes, audit;
  - a diff review.

  Run `e2e-runner` and `pr-test-analyzer`. Verify: the PASS report.

  Result (2026-10-05): PASS.
  - Build, types, lint and format are clean.
  - Backend tests pass with 96 % line coverage. Frontend: 2,613 tests pass with 95.6 % line coverage.
  - The security grep finds no secrets and no real personal data. Writes are Admin-only and audited,
    and the header-injection guards are tested.
  - The whole Playwright suite passes, and the settings spec passes twice in a row.
  - `pr-test-analyzer` raised two HIGH gaps, both now tested:
    - `503 federationSettings.busy`, in the API and on the page;
    - the reply-to guard, with more API cases and a `SmtpEmailSender` compose-phase test.
  - Its MEDIUM gaps are covered in part: the emails conflict on the page, and admin-only values
    absent from `/api/federation`.
  - Left as is:
    - documents when the settings cannot be read: the error handler answers 500, and nothing is
      printed with a fallback name;
    - a scheduler test for a lead-time change between runs.
