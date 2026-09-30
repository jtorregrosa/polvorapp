# Tasks

## 1. Research and shared foundations

- [x] 1.1 Check with Context7 (and gh for real-world usage) the EF Core 10 / Npgsql APIs used in design D3. For each one, record in design.md whether it is supported or which workaround applies: expression indexes on `lower(column)` (`HasIndex` vs `migrationBuilder.Sql`), filtered unique indexes (`HasFilter`), check constraints (`ToTable(t => t.HasCheckConstraint)`), string value converters for enums, and mapping `PostgresException` `CheckViolation`/`UniqueViolation`. Verify: design.md updated.
- [x] 1.2 Move the generic problem helpers (`Problem`, `Conflict`, `Invalid`) from the identity `Problems` class to `PolvorApp.SharedKernel/Http/ProblemResults`. Keep the identity code constants where they are. Verify: `dotnet build` passes and the existing identity and `ProblemDetailsTests` pass unchanged.
- [x] 1.3 Write tests for `IUserDirectory` (`FindAsync` for an unknown id returns null; `FindManyAsync` returns name, email, role and derived status, and skips unknown ids). Then move `UserStatus` to `IdentityAccess.Contracts` and implement the directory in the identity module (`AsNoTracking`). Verify: the new tests and the architecture tests pass.
- [x] 1.4 Review group 1 in parallel with `csharp-reviewer` and `code-reviewer`. Fix CRITICAL/HIGH findings.

## 2. FederationCatalog module and persistence

- [x] 2.1 Create `Modules/FederationCatalog` (implementation and `.Contracts`) with the enums `Side`, `WeaponKind`, `Handedness` and `WeaponSize` and their code helpers, and the `ICatalogUsage` contract (design D10), plus unit tests for the code round-trip and for rejecting unknown codes. Add both projects to `PolvorApp.slnx` and register the module in `AddModules`. Verify: the tests, `ModuleRegistrationTests` and the architecture tests pass.
- [x] 2.2 Write a persistence test: the catalog context excludes the audit table from its migrations. Then add `FederationCatalogDbContext` (schema `catalog`, `MigrationOrder = 20`) with the `Comparsa`, `FiringChiefAssignment` and `WeaponModel` mappings, the indexes and the check constraints from D3, plus a design-time factory. Verify: the test passes.
- [x] 2.3 Generate the initial `catalog` migration. Write integration tests on Testcontainers: `migrate` applies it after identity; the database rejects a duplicate `name_key` (case-insensitive name), a rentable pistol, an arcabuz without handedness, a duplicate non-pistol combination and a duplicate assignment; deleting a comparsa cascades to its assignments. Verify: the tests pass and `docker compose up --build` reaches healthy.
- [x] 2.4 Update `backend/src/Modules/README.md` if a convention changed (shared `ProblemResults`, module-to-module read contracts such as `IUserDirectory`). Verify: the README matches the code.
- [x] 2.5 Review group 2 in parallel with `csharp-reviewer` and `database-reviewer`. Fix CRITICAL/HIGH findings.

## 3. Comparsas API

- [x] 3.1 Write integration tests for `POST /api/comparsas` and `PUT /api/comparsas/{id}`:
  - an Admin creates and edits a comparsa;
  - the name is trimmed and normalised to NFC (a decomposed "é" duplicate gives 409);
  - a case-insensitive duplicate gives 409 `comparsas.nameTaken`, including a race on the unique index;
  - an empty name or an invalid side gives 400 naming the field;
  - an unchanged edit records no audit entry;
  - `ComparsaCreated`/`ComparsaUpdated` are audited with the comparsa id and previous/current values;
  - a FiringChief gets 403.

  Then implement `ComparsaAdministration`, `CatalogInput` and the endpoints. Verify: the tests pass.
- [x] 3.2 Write tests for `POST /api/comparsas/{id}/deactivate` and `/reactivate`: state changes; repeated calls are no-ops without audit; assignments are kept; 404 for an unknown id; FiringChief 403. Then implement them. Verify: the tests pass.
- [x] 3.3 Write tests for `GET /api/comparsas` and `GET /api/comparsas/{id}`:
  - an Admin sees all, sorted by name, with the `side` and `includeInactive` filters (inactive hidden by default);
  - a FiringChief sees only the assigned comparsas, including inactive ones, using a fake assignment source;
  - a FiringChief without assignments gets an empty list;
  - an out-of-scope id gives 404 `comparsas.notFound`.

  Then implement with `ComparsaAccess.Filter`. Verify: the tests pass.
- [x] 3.4 Write tests for `DELETE /api/comparsas/{id}`:
  - an unused comparsa is deleted, then gives 404, and its name can be reused;
  - its assignments are removed and the affected FiringChiefs lose the scope on their next request;
  - a fake `ICatalogUsage` reporting use gives 409 `comparsas.inUse` and nothing is deleted;
  - an unknown id gives 404;
  - one `ComparsaDeleted` entry carries the snapshot and the unassigned user ids;
  - a FiringChief gets 403.

  Then implement the deletion with the row lock from D10. Verify: the tests pass.
- [x] 3.5 Review group 3 in parallel with `csharp-reviewer`, `security-reviewer` (BR-12 scoping, 404 on out-of-scope) and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 4. FiringChief assignments and scope

- [x] 4.1 Write integration tests for `PUT` and `DELETE /api/comparsas/{id}/firing-chiefs/{userId}`:
  - assign an `ACTIVE` and an `INVITED` FiringChief;
  - an `ADMIN` user gives 409 `assignments.notFiringChief`;
  - a `DEACTIVATED` user gives 409 `assignments.userDeactivated`;
  - an inactive comparsa gives 409 `assignments.comparsaInactive`;
  - an unknown user or comparsa gives 404;
  - a repeated assign and an unassign of something missing are idempotent without audit;
  - `FiringChiefAssigned`/`FiringChiefUnassigned` are audited with the comparsa id and user id;
  - a FiringChief gets 403, even for their own comparsa.

  Then implement `AssignmentAdministration`. Verify: the tests pass.
- [x] 4.2 Write tests for `GET /api/comparsas/{id}/firing-chiefs` and `GET /api/firing-chiefs/{userId}/comparsas`:
  - names, emails and statuses come from `IUserDirectory`;
  - an assignment whose user is unknown to the directory is skipped;
  - an unknown user gives 404;
  - one comparsa with two FiringChiefs and one FiringChief with two comparsas are listed correctly;
  - FiringChiefs get 403.

  Then implement both endpoints. Verify: the tests pass.
- [x] 4.3 Write tests for the scope source against the real module:
  - an assignment grants scope on the FiringChief's next request without signing in again, and removing it withdraws the scope;
  - a FiringChief promoted to Admin is scoped to all;
  - deleting a comparsa withdraws it from its FiringChiefs' scope on their next request (moved from 3.4, which needs the real source);
  - the host resolves the catalog `IFiringChiefAssignmentSource`, not the default.

  Then implement `FiringChiefAssignmentSource` and register it. Verify: the tests and the existing `ComparsaScopeTests` pass.
- [x] 4.4 Write the scope guard integration test (design D5): enumerate every endpoint whose route contains `/comparsas/{id}` and assert that a FiringChief without assignments never gets a `2xx`. Verify: the test passes and fails when a deliberately unscoped probe route is added in the test.
- [x] 4.5 Review group 4 in parallel with `csharp-reviewer`, `security-reviewer` (BR-12, SEC-03, no user data leaked to FiringChiefs) and `database-reviewer`. Fix CRITICAL/HIGH findings.

## 5. Weapon catalogue API

- [x] 5.1 Write integration tests for `POST` and `PUT /api/weapon-models`:
  - an Admin creates a rentable trabuco;
  - a pistol without attributes is accepted;
  - a rentable pistol gives 400 `errors.rentable = pistolNotRentable` (BR-07);
  - an arcabuz without handedness gives 400 naming `handedness`;
  - invalid enum values give 400 naming the field;
  - a free kind/side combination is accepted;
  - a duplicate label (case-insensitive) gives 409 `weaponModels.labelTaken`;
  - a duplicate non-pistol combination gives 409 `weaponModels.combinationTaken`;
  - an unchanged edit is not audited;
  - create and update are audited with their fields;
  - a FiringChief gets 403.

  Then implement `WeaponModelAdministration` and the endpoints. Verify: the tests pass.
- [x] 5.2 Write tests for `GET /api/weapon-models` (sorted by label, `kind` and `includeInactive` filters, readable by FiringChiefs) and `GET /api/weapon-models/{id}` (404 unknown), and for `/deactivate` and `/reactivate` (no-ops not audited, FiringChief 403). Then implement them. Verify: the tests pass.
- [x] 5.3 Write tests for `DELETE /api/weapon-models/{id}`:
  - an unused model is deleted, then gives 404, and its label can be reused;
  - a fake `ICatalogUsage` reporting use gives 409 `weaponModels.inUse`;
  - an unknown id gives 404;
  - `WeaponModelDeleted` is audited with a snapshot;
  - a FiringChief gets 403.

  Then implement it. Verify: the tests pass.
- [x] 5.4 Update `docs/data-model.md`:
  - WeaponModel gets `active`, and side, handedness and size are optional for pistols;
  - Comparsa name uniqueness;
  - FiringChiefAssignment rules, citing the maintainer decisions: free kind/side, assignments managed from both pages;
  - deletion of comparsas and models: blocked while referenced, and assignments are removed with the comparsa.

  In `docs/glossary.md`, state that a FiringChief can also be an arquebusier (separate `User` and `Arquebusier` records) and that arquebusiers are never users. Add to `docs/open-questions.md`, for #5, whether a `User` needs a link to their `Arquebusier` record. Verify: the docs match the spec and the links resolve.
- [x] 5.5 Review group 5 in parallel with `csharp-reviewer` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 6. Synthetic catalogue data

- [x] 6.1 Write tests for `CatalogSeeder`:
  - 4 fictional comparsas, one inactive;
  - the assignments Jefe Uno → Norte and Sur, and Jefa Dos → Norte;
  - 9 models covering every kind, with one inactive and a non-rentable pistol;
  - running it twice creates nothing new;
  - outside Development and Testing it refuses a database with non-synthetic comparsas;
  - the seeded user ids equal `IdentitySeeder.Users`.

  Then implement it (Order 20). Verify: the tests pass and `docker compose --profile seed run api-seed` succeeds.
- [x] 6.2 Update `docs/development.md` (what the seed now contains, and which comparsas each seeded FiringChief sees). Verify: the documented sign-in shows those comparsas.
- [x] 6.3 Review group 6 with `csharp-reviewer` and `security-reviewer` (SEC-11, no real names). Fix CRITICAL/HIGH findings.

## 7. Frontend foundation

- [x] 7.1 Regenerate `contracts/openapi.json` and the orval client. Verify: the contract check in CI passes locally (`npm run generate:api` then `git diff` only shows the new endpoints and `UserStatus` in contracts), and `npm run typecheck` passes.
- [x] 7.2 Add the `catalog` namespace (es-ES, ca-ES-valencia, en) with the D8 keys, add `common` `nav.comparsas` and `nav.weaponModels`, and extend the typed-keys declaration. Verify: `npm run check-i18n` and `npm run typecheck` pass.
- [x] 7.3 Write tests for the `catalog` status mapping (`ACTIVE`, `INACTIVE`). Then add it to `components/app/status.ts` and `docs/design/status.md`. Verify: the tests and `design-docs.test.ts` pass.
- [x] 7.4 Update the shell tests: the Comparsas entry is shown to every role and Weapon models only to Admins. Then add both navigation entries. Verify: the tests pass.
- [x] 7.5 Review group 7 in parallel with `typescript-reviewer` and `react-reviewer`. Fix CRITICAL/HIGH findings.

## 8. Frontend screens

- [x] 8.1 Write tests (behaviour and axe) for `ComparsasPage`:
  - an Admin gets filters, "include inactive" and the "New comparsa" action;
  - a FiringChief gets the list without actions;
  - a FiringChief without assignments gets the empty state;
  - side and status are translated.

  Then implement it with MSW handlers. Verify: the tests pass.
- [x] 8.2 Write tests for `ComparsaFormPage` and `ComparsaDetailPage`:
  - create and edit with validation;
  - a 409 `nameTaken` message;
  - deactivate and reactivate confirmed;
  - delete confirmed, with the count of FiringChiefs losing access, returning to the list; a 409 `inUse` is shown in the dialog with the deactivate hint;
  - the read-only view for FiringChiefs;
  - an unknown or out-of-scope id shows `NotFoundPage`.

  Then implement them. Verify: the tests pass.
- [x] 8.3 Write tests for `AssignmentList` and the comparsa FiringChiefs section:
  - candidates exclude deactivated, Admin and already-assigned users;
  - adding and removing, with the removal confirmed;
  - a 409 reason is shown translated and the list is refetched.

  Then implement them. Verify: the tests pass.
- [x] 8.4 Write tests for `UserComparsasSection` on the identity `UserDetailPage`:
  - it is shown for FiringChiefs;
  - it is shown with a "no effect" notice for an Admin who still has assignments;
  - it is hidden for an Admin without assignments;
  - candidates exclude inactive and assigned comparsas.

  Then implement it and render it from `UserDetailPage`. Verify: the tests, including the existing `UsersPages.test.tsx`, pass.
- [x] 8.5 Write tests for `WeaponModelsPage` and `WeaponModelFormPage`:
  - filters;
  - translated enum columns with "—" for missing pistol attributes;
  - for a pistol, rentable is disabled and forced off and the attributes are optional;
  - required attributes for trabuco and arcabuz;
  - the 409 label and combination messages;
  - deactivate and reactivate confirmed;
  - delete confirmed, and a 409 `inUse` shown with the deactivate hint.

  Then implement them and wire the routes (`RequireAdmin` for the model and create/edit routes). Verify: the tests pass.
- [x] 8.6 Review translations in the three locales (`i18n-sync`) and flag new Valencian texts for native review. Verify: `npm run check-i18n` and `npm run lint` pass.
- [x] 8.7 Review group 8 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 9. End-to-end tests

- [x] 9.1 Write Playwright flows against the seeded compose stack:
  - an Admin creates a comparsa, assigns a FiringChief from the comparsa page and removes it from the user page, then deletes the comparsa;
  - an Admin creates a pistol and cannot make it rentable;
  - the seeded FiringChief sees only their comparsas, gets the not-found page for another comparsa's URL and has no Weapon models entry;
  - axe on the comparsa, comparsa detail and weapon model pages, in both themes.

  Verify: the full suite passes against compose.
- [x] 9.2 Review group 9 with `e2e-runner` (Playwright only, ADR-0011) and `pr-test-analyzer`. Fix CRITICAL/HIGH findings.

## 10. Verification

- [x] 10.1 Update the `docs/mvp.md` status of change #4 and run `doc-updater` for drift between the docs and the code. Verify: no drift is reported.
- [x] 10.2 Run `verification-loop`:
  - backend build, format and tests, with coverage ≥ 80 % for `FederationCatalog`;
  - frontend lint, typecheck, check-i18n, tests and build;
  - the contract check;
  - a security grep for real data and secrets;
  - a diff review.

  Also run the full Playwright suite against compose. Verify: a PASS report and green CI on the pull request.
