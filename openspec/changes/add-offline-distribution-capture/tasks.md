# Tasks

## 1. Research

- [x] 1.1 Check with Context7, and `gh search code` for real-world usage, what D2, D4 and D5 rely on:
  - `idb` (promise wrapper, upgrade callback, transactions) with React 19 and Vite;
  - `fake-indexeddb` under Vitest jsdom;
  - vite-plugin-pwa / Workbox `generateSW`: `navigateFallback`, `globPatterns` and denylist;
  - `navigator.storage.persist()`;
  - Playwright `context.setOffline` with a service worker;
  - Npgsql/EF Core functional unique index on `upper(column)` with a filter;
  - mapping a `23505` unique violation to a domain code.

  Record versions and workarounds in design.md. Verify: design.md is updated and has no new open
  question beyond Q-43.

## 2. Handover model and migration

- [x] 2.1 Write the persistence tests first: the unique handover per (day, holder), the
  case-insensitive flask number per day, cascade on entry removal, `SET NULL` on collector
  removal, and `RESTRICT` on day deletion. Then add `Handover`, its `DbContext` mapping and the
  migration with the cross-schema keys (D1). Verify: the Testcontainers tests pass and the
  migration applies to an empty and a seeded database.
- [x] 2.2 Write the coverage test change, then register `distribution.handovers` with
  `DistributionPersonalData`: export by holder or collector, erasure keeps them (D8). Verify:
  `PersonalDataCoverageTests` and the erasure integrity tests pass.
- [x] 2.3 Block deleting a day with handovers: write the API test for 409
  `distribution.hasHandovers`, then implement it. Verify: the test passes and the existing day
  tests stay green.
- [x] 2.4 Review group 2 in parallel with `csharp-reviewer`, `database-reviewer` and
  `security-reviewer`. Fix CRITICAL/HIGH findings.

## 3. Capture package and sync API

- [x] 3.1 Write the API tests for `GET /distributions/{id}/capture`:
  - rows and numbers equal the powder list;
  - proxies and existing handovers included;
  - no license or contact data;
  - `no-store`;
  - audited before sending, 503 when the audit fails;
  - 409 `captureNotPowder` and `editionNotInProgress`, 403 for a FiringChief, 404 unknown.

  Then implement it, reusing `DistributionLists` and `DistributionNumbering` (D2). Verify: the
  tests pass.
- [x] 3.2 Write the sync tests:
  - recorded;
  - `alreadyRecorded` on the same id and data, and `handoverChanged` on the same id with
    different data;
  - per-item refusals: `alreadyHandedOver` with `existing`, `flaskNumberTaken` (case-insensitive),
    `flaskNumberRequired`, `flaskNumberNotRented`, `notInList`, `proxyNotValid`,
    `editionNotInProgress`, `invalid` for lengths;
  - the server stores the entry's current kg;
  - at most 100 items, 403 for a FiringChief, the anti-forgery token required.

  Then implement the endpoint with one transaction per item (D2, D3). Verify: the tests pass.
- [x] 3.3 Write the concurrency and resilience tests: two parallel syncs racing on one entry, and
  on one flask number for two entries, each recorded once with the other refused; a lock timeout
  or an audit failure answers `busy` and stores nothing. Then implement the unique-violation
  mapping and `DistributionWriteGuard` use. Verify: the tests pass repeatedly (5 runs).
- [x] 3.4 Add the `HandoverSync` rate-limit policy (configurable, 60/min/user, raised in CI like
  the others), with its 429 test. Verify: the test passes and `.env.example` and the CI env list
  the new key.
- [x] 3.5 Write the tests, then implement the undo `DELETE /handovers/{id}?version`: audited, 409
  `modified`, 409 `editionNotInProgress`, 403. Also add `handedOverCount` and `holderCount` to the
  plan response. Verify: the tests pass and the OpenAPI document and the orval client are
  regenerated.
- [x] 3.6 Write the audit tests for the three actions (no names or DNI/NIE), then add the actions
  to `DistributionAuditActions` and the audit-log labels in the three locales. Verify: the tests
  pass and `check-i18n` passes.
- [x] 3.7 Review group 3 in parallel with `csharp-reviewer`, `security-reviewer`,
  `database-reviewer` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 4. Lists, GDPR export and seed

- [x] 4.1 Write the `DistributionLists` unit tests and the document tests (powder list filled with
  the flask number, traceability and "collected by", plus "N handovers recorded"; weapons list
  unchanged), then implement D6. Verify: the tests pass, and a generated PDF and Excel file look
  right.
- [x] 4.2 Write the privacy export test for the handovers sheet (holder and proxy rows by role
  only), then implement it, with its translated sheet headers in the three locales. Verify: the
  tests pass and `check-i18n` passes.
- [x] 4.3 Seed synthetic handovers for the past edition, with invented flask numbers, fixed ids
  and safe to run again. Verify: seeder tests pass, and a re-seed is a no-op.
- [x] 4.4 Review group 4 in parallel with `csharp-reviewer` and `security-reviewer`. Fix
  CRITICAL/HIGH findings.

## 5. On-device store and sync engine (frontend)

- [x] 5.1 Add `idb` and `fake-indexeddb` (dev), or document the raw-IndexedDB fallback chosen in
  1.1. Write the store tests (packages, queue, meta, owner, expiry purge, user switch), then
  implement `features/distribution/offline/store` (D4). Verify: the Vitest tests pass and
  `npm audit` reports nothing new.
- [x] 5.2 Write the sync-engine tests:
  - batching at 100;
  - outcome handling;
  - `busy` stays pending with backoff;
  - a 401 keeps the queue and stops;
  - the network `TypeError` counts as offline;
  - triggers on `online`, on open and on demand.

  Then implement `useHandoverSync` and the connectivity hook. Verify: the tests pass with at
  least 90 % coverage of the engine.
- [x] 5.3 Write the tests for the sign-out guard (a dialog with the pending count, clearing after
  confirmation) and for clearing on a user switch, then wire them into the session code without
  touching IndexedDB from the global 401 handler. Verify: the tests pass, including the existing
  session tests.
- [x] 5.4 Review group 5 in parallel with `typescript-reviewer`, `react-reviewer`,
  `security-reviewer` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 6. Screens and PWA

- [x] 6.1 Change the PWA config (D5): `globPatterns` with fonts and icons, navigation fallback
  for the capture route, and still no `runtimeCaching`. Extend the existing "API never cached"
  test. Verify: the build's generated `sw.js` precaches `index.html` and the fonts, and has no
  `/api` route.
- [x] 6.2 Write the tests and stories, then build the powder-day card additions: "N of M
  delivered", "Prepare for offline capture" (with `storage.persist()` and its result) and "Open
  capture". Also make "Delete" say why it is not offered when the day has handovers. Verify: the
  tests, axe and the catalogue test pass.
- [x] 6.3 Write the tests, then build the capture route outside `RequireSession`:
  - background session check;
  - "not allowed" for a FiringChief or another user;
  - "Sign in to sync" on a 401;
  - the status bar;
  - the searchable grouped list with state tags;
  - the conflicts section.

  Verify: the tests and axe pass at 360 px and wide.
- [x] 6.4 Write the tests, then build the handover panel (`EditSheet`, React Hook Form + Zod):
  - collector radio;
  - flask number only for rented flasks, and unique against the package and the queue (naming
    the other number);
  - traceability fields;
  - edit and remove for unsynced handovers;
  - "Undo handover" online only, with a confirmation naming the holder and the flask.

  Verify: the tests and axe pass.
- [x] 6.5 Add every new string to `distribution.json` (and `ui.json` if any) in es-ES,
  ca-ES-valencia and en, with the problem codes mapped in `problems.ts`. Verify: `check-i18n`
  passes, and the catalogue renders the new stories in the three locales.
- [x] 6.6 Review group 6 in parallel with `react-reviewer`, `typescript-reviewer`,
  `a11y-architect` and `security-reviewer`. Fix CRITICAL/HIGH findings.

## 7. Documentation

- [x] 7.1 Update the documentation. Verify: the docs match the specs, and `doc-updater` reports
  no drift.
  - `docs/compliance.md`: add SEC-14, and the SEC-05/06/09 notes;
  - `docs/data-model.md`: Handover implemented, and the lists filled from handovers;
  - `docs/glossary.md`: handover, capture package, `distributionNumber` copy;
  - `docs/use-cases.md`: UC-21 implemented as the powder day only;
  - `docs/open-questions.md`: Q-43 open, no longer blocking;
  - `docs/mvp.md`: the change row;
  - `docs/design/README.md`: the capture screen.

## 8. End-to-end and verification

- [x] 8.1 Write `e2e/serial-state/offline-capture.spec.ts` (D9):
  - validate Norte's order through the API;
  - prepare the device and go offline;
  - reload the capture route from the precache and record handovers, including an on-device
    flask conflict;
  - go online and sync;
  - a second context makes a server conflict that shows on the first;
  - the powder list download is filled in;
  - the sign-out warning;
  - axe on the capture screen;
  - undo to restore the data.

  Verify: the spec passes twice in a row on the compose stack, built from PowerShell, and the
  full E2E suite stays green.
- [x] 8.2 Run `verification-loop`:
  - build, types, lint, Prettier and `check-i18n`;
  - backend and frontend tests with at least 80 % coverage on the new code;
  - `dotnet format --verify-no-changes`;
  - a security grep and a diff review.

  Then run `e2e-runner` and `pr-test-analyzer`. Verify: a PASS report, with findings and
  follow-ups recorded in design.md.
