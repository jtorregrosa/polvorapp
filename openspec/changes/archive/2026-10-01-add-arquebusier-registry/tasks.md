# Tasks

## 1. Research and shared foundations

- [x] 1.1 Check with Context7 (and gh for real-world usage) the EF Core 10 / Npgsql APIs used in design D3:
  - `xmin` as a concurrency token;
  - complex/owned types mapped to columns of the same table, with nullable members;
  - `DateOnly` ↔ `date`;
  - adding cross-schema foreign keys with `migrationBuilder.Sql`;
  - the `PostgresException` `ConstraintName` for unique, check and foreign-key violations.

  Record in design.md what is supported or which workaround applies. Verify: design.md updated.
- [x] 1.2 Write unit tests for `SharedKernel.Time.FederationCalendar.Today(TimeProvider)`: the Europe/Madrid date on both sides of midnight UTC, in winter and summer time. Then implement it. Verify: the tests pass on Windows and in the Linux CI container.
- [x] 1.3 Create `contracts/test-vectors/national-ids.json` with synthetic cases:
  - valid DNI and NIE (X/Y/Z);
  - spaces, hyphens and lower case;
  - wrong check letter;
  - 7 digits;
  - a Cyrillic "Х" and a Greek "Υ" prefix;
  - full-width digits;
  - empty.

  Write the xUnit `[Theory]` that reads the file, then implement the server validator (design D4). Verify: the tests pass.
- [x] 1.4 Write integration tests for `ICatalogDirectory`:
  - an unknown comparsa or model returns null;
  - `FindComparsasAsync` and `FindWeaponModelsAsync` return summaries with `Active` and skip unknown ids.

  Then add the contract and records to `FederationCatalog.Contracts` and implement it in the catalog module with `AsNoTracking` queries (design D2). Verify: the tests and the architecture tests pass.
- [x] 1.5 If the registry needs the name character checks of `CatalogInput`, move them to `SharedKernel` with no behaviour change, adding direct unit tests (it had none). Verify: the catalog tests pass unchanged.
- [x] 1.6 Record two maintainer answers in the docs:
  - Q-52: no link between `User` and `Arquebusier`, in `docs/open-questions.md` and `docs/glossary.md` (FiringChief row);
  - Q-27: A-PROF is renewed every year, confirmed. Remove "to confirm" and the ❓ from `docs/glossary.md`, `docs/open-questions.md` and the License line of `docs/data-model.md`.

  Verify: the docs updated.
- [x] 1.7 Review group 1 in parallel with `csharp-reviewer` and `code-reviewer`. Fix CRITICAL/HIGH findings.

## 2. ArquebusierRegistry module and persistence

- [x] 2.1 Create `Modules/ArquebusierRegistry` (implementation and `.Contracts`) with the coded enums `ArquebusierStatus`, `Gender`, `LicenseType` and `LicenseStatus` (design D1). Add both projects to `PolvorApp.slnx` and the `backend/Dockerfile` restore layer, and register the module after `FederationCatalog`. Verify: `CodedEnumsTests`, `ModuleRegistrationTests` and the architecture tests pass, and `docker compose build api` succeeds.
- [x] 2.2 Write persistence tests: the registry context excludes the audit table from its migrations and is listed in `ModelDriftTests`. Then add `ArquebusierRegistryDbContext` (schema `registry`, `MigrationOrder = 30`) with the `Arquebusier` mapping (license as four plain columns behind a `License` value object, see the task 1.1 note; `xmin` version) and the `OwnedWeapon` mapping, the indexes and the check constraints from D3, plus a design-time factory. Verify: the tests pass.
- [x] 2.3 Generate the initial `registry` migration, adding the two cross-schema foreign keys with `migrationBuilder.Sql`. Write Testcontainers integration tests:
  - `migrate` applies it after `catalog`;
  - the database rejects a duplicate `national_id`, `federation_id` and ownership guide;
  - it rejects an inconsistent license (pending with dates, `expires_on <= issued_on`, dates without type);
  - it rejects an unknown comparsa or weapon model id;
  - deleting an arquebusier cascades to its owned weapons;
  - both foreign keys exist with `ON DELETE NO ACTION` and deleting a referenced comparsa raises a foreign-key violation.

  Verify: the tests pass and `docker compose up --build` reaches healthy.
- [x] 2.4 Write integration tests for `RegistryCatalogUsage`:
  - deleting a comparsa that has an arquebusier gives 409 `comparsas.inUse`;
  - deleting a model referenced by an owned weapon gives 409 `weaponModels.inUse`;
  - with the usage check removed from DI, the foreign key still gives 409;
  - after the last arquebusier is transferred away, the comparsa can be deleted.

  Then implement and register it. Verify: the tests pass.
- [x] 2.5 Document the cross-module foreign key convention (one direction, migration order, no queries on another module's tables) and `ICatalogDirectory` in `backend/src/Modules/README.md`. Verify: the README matches the code.
- [x] 2.6 Review group 2 in parallel with `csharp-reviewer` and `database-reviewer`. Fix CRITICAL/HIGH findings.

## 3. Arquebusier rules and API

- [x] 3.1 Write unit tests for `RegistryInput`. They cover each field rule of the spec "Arquebusier data":
  - required fields, lengths, federationId range, birthDate bounds, email and phone formats, gender and status codes;
  - control and invisible characters in names;
  - normalisation (trim, NFC, lower-case email, nationalId via D4);
  - the license rules (pending vs issued, `notAfterIssued`, `future`);
  - the BR-03 defaults for AE and A_PROF, including 29 February;
  - `trainingCompletedOn` not in the future.

  Then implement it. Verify: the tests pass.
- [x] 3.2 Write integration tests for `POST /api/arquebusiers`:
  - a FiringChief registers in their comparsa, and the default status is `ACTIVE`;
  - an Admin registers in any comparsa;
  - an out-of-scope comparsa gives 404 `arquebusiers.comparsaNotFound`;
  - an inactive comparsa gives 409 `arquebusiers.comparsaInactive`;
  - a duplicate nationalId or federationId gives 409 with no data of the other record, including a concurrent race on the unique index;
  - invalid fields give 400 naming them;
  - `ArquebusierRegistered` is audited with the comparsa id and no personal values.

  Then implement `ArquebusierAdministration.RegisterAsync` and the endpoint. Verify: the tests pass.
- [x] 3.3 Write integration tests for `GET /api/arquebusiers` and `GET /api/arquebusiers/{id}`:
  - an Admin sees all, filtered by `comparsaId` and `status`;
  - a FiringChief with two comparsas sees both and no other;
  - a FiringChief without assignments gets an empty list;
  - an out-of-scope id gives 404;
  - rows are sorted by last name then first name in Spanish order (accents, "ñ");
  - `licenseStatus` is derived (`VALID`, `EXPIRED` from a fake `TimeProvider`, `PENDING`, null);
  - the detail includes the comparsa name, the owned weapons with model summaries, and `version`.

  Then implement both. Verify: the tests pass.
- [x] 3.4 Write integration tests for `PUT /api/arquebusiers/{id}`:
  - a FiringChief edits personal data, license, course and status;
  - the comparsa cannot change;
  - an omitted optional field is cleared;
  - an outdated `version` gives 409 `arquebusiers.modified`;
  - an unchanged edit gives 200 with no save and no audit;
  - `ArquebusierUpdated` lists only the changed field names, without values;
  - an arquebusier of an inactive comparsa stays editable;
  - an out-of-scope id gives 404.

  Then implement it. Verify: the tests pass.
- [x] 3.5 Generalise the #4 scope guard test to a list of scoped route prefixes that includes `/arquebusiers/{id}`: a FiringChief without assignments, and one assigned elsewhere, get only 403 or 404 from every such route. Verify: the test passes and fails when a scope filter is removed on purpose.
- [x] 3.6 Write a test that a rejected duplicate and a validation failure log no nationalId, name or contact value (NFR-12). Fix any log that does. Verify: the test passes.
- [x] 3.7 Update `docs/data-model.md`:
  - the Arquebusier field rules (formats, optional email and phone, gender codes, the ID photo deferred to #6);
  - the License status derivation;
  - the OwnedWeapon rules (unique guide number, weapon number not unique).

  Add `Gender` and the `LicenseStatus` codes to `docs/glossary.md`. Verify: the docs match the spec.
- [x] 3.8 Review group 3 in parallel with `csharp-reviewer`, `security-reviewer` (BR-12 scoping, personal data in responses, logs and audit) and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 4. Owned weapons, transfer and deletion

- [x] 4.1 Write integration tests for the owned-weapon endpoints:
  - a FiringChief adds a weapon to an arquebusier in scope, with any active model including a pistol, and the guide number is upper-cased;
  - an unknown model gives 400 `weaponModelId: notFound`;
  - an inactive model on add or on a model change gives 409 `ownedWeapons.modelInactive`;
  - an edit that keeps an inactive model succeeds;
  - a duplicate guide in any comparsa, compared ignoring case, gives 409 `ownedWeapons.guideTaken`, including a race;
  - the same weapon number twice is allowed;
  - an outdated version gives 409;
  - an out-of-scope arquebusier or a weapon of another arquebusier gives 404;
  - the audit entries are recorded without numbers.

  Then implement `OwnedWeaponAdministration` and the endpoints. Verify: the tests pass.
- [x] 4.2 Write integration tests for `POST /api/arquebusiers/{id}/transfer`:
  - an Admin moves an arquebusier with owned weapons;
  - the FiringChief of the source comparsa then gets 404 and the target's FiringChief sees it;
  - an inactive target gives 409, the same comparsa gives 409 `sameComparsa`, and an unknown target gives 404;
  - a FiringChief gets 403;
  - `ArquebusierTransferred` records both comparsas;
  - a transfer racing with the deletion of the target comparsa leaves a consistent state (409 or 404, never a dangling reference).

  Then implement it (design D10). Verify: the tests pass.
- [x] 4.3 Write integration tests for `DELETE /api/arquebusiers/{id}`:
  - a FiringChief deletes in scope and an Admin anywhere;
  - the owned weapons are removed;
  - the arquebusier then gives 404;
  - nationalId, federationId and guide numbers can be registered again;
  - an out-of-scope id gives 404;
  - `ArquebusierDeleted` holds `ownedWeaponCount` and no personal data, and no earlier audit entry of that arquebusier holds personal values.

  Then implement it. Verify: the tests pass.
- [x] 4.4 Review group 4 in parallel with `csharp-reviewer`, `security-reviewer` and `database-reviewer` (locks, foreign-key races). Fix CRITICAL/HIGH findings.

## 5. Synthetic seed

- [x] 5.1 Write seed tests:
  - running twice creates each arquebusier and owned weapon once;
  - every seeded nationalId passes the D4 validator;
  - the seed covers `ACTIVE`/`RESERVE`, every license status, no license, course done and not done, and a pistol;
  - the seeded FiringChiefs see only their comparsas' arquebusiers;
  - the non-local guard refuses a database with non-synthetic arquebusiers.

  Then implement `RegistrySeeder` (design D9). Verify: the tests pass and `docker compose run api seed` succeeds locally.
- [x] 5.2 Review group 5 with `security-reviewer` (SEC-11: nothing real-looking beyond synthetic patterns). Fix CRITICAL/HIGH findings.

## 6. API contract and frontend foundations

- [x] 6.1 Regenerate `contracts/openapi.json` and the orval client. Verify: `OpenApiDocumentTests` passes, the generated hooks for `/arquebusiers` exist, and `npm run typecheck` passes.
- [x] 6.2 Write Vitest tests that read `contracts/test-vectors/national-ids.json`, then implement the TypeScript national ID validator in `features/arquebusier-registry/`. Verify: the same vectors pass on both sides.
- [x] 6.3 Write tests and an axe check for a new `DateInput` composite (native date input inside `FormField`: label, error, `min`/`max`). Then implement it with a story, and document it in `docs/design/patterns.md` (forms). Verify: the tests, the Storybook build and the composite catalogue test pass.
- [x] 6.4 Add the `registry` i18n namespace (es-ES, ca-ES-valencia, en) with the enum, error and validation keys from design D8, `common:nav.arquebusiers`, the typed-keys declaration, the navigation entry and the routes with placeholder pages. Verify: the translation completeness test and the navigation test pass.
- [x] 6.5 Review group 6 in parallel with `typescript-reviewer`, `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 7. Registry screens

- [x] 7.1 Write component tests for `ArquebusiersPage`:
  - columns, status and license badges, and "No license";
  - accent- and case-insensitive search by name, nationalId and federationId;
  - comparsa and status filters, with the comparsa filter hidden for a single comparsa;
  - the empty state for a FiringChief without assignments;
  - the search term is not added to the URL;
  - axe passes.

  Then implement the page with translations in the 3 locales. Verify: the tests pass.
- [x] 7.2 Write component tests for the register and edit form (`ArquebusierFields`):
  - the comparsa is pre-selected when there is one, and only active comparsas in scope are offered;
  - nationalId check-letter feedback appears on blur;
  - expires-on is pre-filled for AE and A_PROF until edited;
  - the pending license disables the dates;
  - server field errors and `code`s are shown translated;
  - `409 modified` shows the banner and reloads;
  - axe passes.

  Then implement `ArquebusierFormPage` and the edit part of `ArquebusierDetailPage` with translations. Verify: the tests pass.
- [x] 7.3 Write component tests for the owned weapons section and `OwnedWeaponFormPage`:
  - list with translated model attributes;
  - add and edit, with only active models plus the current inactive one;
  - remove with confirmation;
  - the guide-taken and model-inactive errors are shown.

  Then implement them with translations. Verify: the tests pass.
- [x] 7.4 Write component tests for transfer and delete:
  - the transfer section exists only for Admins, offers the other active comparsas, and its confirmation names both comparsas;
  - the delete confirmation names the arquebusier, says it cannot be undone, and suggests Reserve for someone who only stops firing;
  - after deletion the user returns to the list with a focused notice;
  - cancelling changes nothing;
  - the inactive-comparsa banner is shown.

  Then implement both with translations. Verify: the tests pass.
- [x] 7.5 Check the screens at 375 px and in dark mode in Storybook or the running app, and fix any overflow (NFR-01). Verify: no horizontal page scroll, and touch targets meet WCAG 2.5.8.
- [x] 7.6 Review group 7 in parallel with `react-reviewer`, `typescript-reviewer`, `a11y-architect` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 8. End-to-end and verification

- [x] 8.1 Write Playwright flows on the seeded data:
  - a FiringChief registers an arquebusier with an AE license and course, adds an owned weapon, edits the phone, sets `RESERVE` and deletes the arquebusier;
  - a FiringChief cannot open another comparsa's arquebusier by URL (not-found page);
  - an Admin transfers an arquebusier, and the source FiringChief no longer sees it;
  - an Admin cannot delete a comparsa that has arquebusiers.

  Verify: `npm run e2e` passes locally and in CI.
- [x] 8.2 Update `docs/mvp.md` (#5 status) and any doc the change touched, with `doc-updater`. Verify: the docs match the code and specs.
- [x] 8.3 Run `verification-loop`: build, types, lint, backend and frontend tests with coverage ≥ 80 % for the registry module and endpoints, a security grep (no real data, no secrets, no personal values in logs or audit), and a diff review. Run `e2e-runner` (Playwright only) and `pr-test-analyzer` on the change. Verify: the PASS report is attached to the pull request, and the findings and follow-ups are summarised before archive.
