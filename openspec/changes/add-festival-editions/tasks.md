# Tasks

## 1. Research

- [x] 1.1 Check with Context7 (and `gh search code` for real-world usage) the APIs used in design D1–D3 and D10:
  - EF Core 10 / Npgsql: `decimal` mapped to `numeric(6,2)`, owned types with nullable columns, a partial unique index on a constant expression (`HasFilter`), and mapping its violation by constraint name;
  - System.Text.Json: reading `decimal` under `NumberHandling.Strict` and writing two decimals;
  - `Intl.NumberFormat` with `style: 'currency'` for es-ES, ca-ES-valencia and en;
  - Playwright project `dependencies` for the `serial-state` project.

  Record versions and any workaround in design.md. Verify: design.md updated, with no open question left.

## 2. Module skeleton and data model

- [x] 2.1 Create `PolvorApp.FestivalEditions` and `.Contracts` (design D1): `FestivalEditionsModule` (`MigrationOrder = 40`), `FestivalEditionsDbContext` (schema `editions`, `AddAuditTrail`), the design-time factory, and the `EditionStatus` coded enum (`DRAFT`, `IN_PROGRESS`, `CLOSED`). Register the module in `Program.cs`, `PolvorApp.slnx`, the `Dockerfile` and `ModelDriftTests`. Verify: `dotnet build` passes, and the architecture tests, `CodedEnumsTests` and `ModelDriftTests` pass.
- [x] 2.2 Add the entities `FestivalEdition` (with `ordersOpen`, the four nullable price columns and its `xmin` version), `EditionWeaponModel` and `CalendarMilestone`, their configuration (constraints, indexes, the partial unique index `ux_festival_editions_in_progress`, the check that only an `IN_PROGRESS` edition has open orders), and the initial migration, including the raw-SQL FK to `catalog.weapon_models` with `ON DELETE NO ACTION`. Write Testcontainers tests:
  - a second `IN_PROGRESS` edition violates the index;
  - open orders on a `DRAFT` or `CLOSED` edition violate the check;
  - deleting a model referenced by `edition_weapon_models` violates the FK;
  - deleting an edition cascades to its models and milestones;
  - prices round-trip exactly as `numeric(6,2)`.

  Verify: the tests pass, and `migrate` applies the migration to a fresh database.
- [x] 2.3 Review group 2 in parallel with `csharp-reviewer` and `database-reviewer`. Fix CRITICAL/HIGH findings.

## 3. Edition rules and administration

- [x] 3.1 Write unit tests for `EditionInput.Read`, covering every blocking rule of "Festival editions (UC-10)" and "Edition prices":
  - year range;
  - festival dates required, within the year and in order;
  - each window date after the festival start is `invalid`;
  - `ordersCloseOn` before `ordersOpenOn` is `invalid`;
  - prices of 0, 9999.99, -1, 4.555 and 10000.

  Then implement it, reporting all errors at once with field keys and reasons. Verify: the tests pass.
- [x] 3.2 Write unit tests for the checks in design D3:
  - `EditionStatusMoves.Check` over every pair of the three statuses, where only adjacent moves pass;
  - completeness when starting an edition, listing each missing field;
  - leaving `IN_PROGRESS` with open orders is refused.

  Then implement them. Verify: the tests pass.
- [x] 3.3 Add `WeaponModelSummary.Rentable` to the catalog contract (design D7), and fill it in `CatalogDirectory`. Verify: the catalog and registry tests pass unchanged, plus one new assertion that `Rentable` is returned.
- [x] 3.4 Write endpoint tests for creating, reading, listing, updating and deleting editions (D4–D6):
  - `201` with the copy from the latest earlier edition (prices, only the still-rentable models) and an empty first edition;
  - `409 editions.yearTaken`;
  - `400` naming the fields;
  - `409 editions.modified` on an outdated version;
  - an unchanged update is not audited;
  - Admin edits of an edition in progress with closed orders, and of a `CLOSED` edition;
  - delete only in `DRAFT` (`409 editions.notDraft`);
  - FiringChief `403` on every write;
  - FiringChief lists without drafts, and `404` on a draft;
  - list newest first, with `ordersOpen`.

  Then implement `EditionAdministration`, the queries and the endpoints. Verify: the tests pass.
- [x] 3.5 Write endpoint tests for `POST /api/editions/{id}/status`:
  - each forward and back move;
  - `invalidTransition` from `DRAFT` to `CLOSED`;
  - `incomplete` with `missing`;
  - `ordersOpen` when closing or sending back an edition with open orders;
  - `anotherInProgress` with `inProgressYear`, including two concurrent starts of different editions where exactly one wins;
  - reopening a `CLOSED` edition while another is in progress;
  - `modified`;
  - FiringChief `403`;
  - an audit entry with both statuses, and none on rejection.

  Then implement it. Verify: the tests pass.
- [x] 3.6 Write endpoint tests for `POST /api/editions/{id}/orders`:
  - open, close and reopen on the edition in progress, each audited `EditionOrdersOpened` / `EditionOrdersClosed`;
  - repeating the same state is not audited;
  - `notInProgress` for a `DRAFT` and a `CLOSED` edition;
  - `modified`;
  - FiringChief `403`.

  Then implement it. Verify: the tests pass.
- [x] 3.7 Write endpoint tests for `PUT /api/editions/{id}/weapon-models`:
  - set replacement;
  - `notFound` and `notRentable`, including a pistol;
  - a model that is already in the set, inactive since, can be kept or removed;
  - `offered` false for it in the response;
  - the audit with added and removed models;
  - FiringChief `403`.

  Then implement it. Verify: the tests pass.
- [x] 3.8 Write endpoint tests for milestones:
  - add, edit and remove;
  - order by date, then title;
  - `title` blank, too long or with a line break is `400`;
  - the 51st is `409 editions.tooManyMilestones`;
  - FiringChief `403`;
  - audited.

  Then implement them. Verify: the tests pass.
- [x] 3.9 Write tests for `GET /api/editions/current` and `IEditionDirectory` (design D7):
  - none when only `CLOSED` and `DRAFT` editions exist;
  - `ordersOpen` true and false on the edition in progress;
  - `nextWindow` relative to a fake `TimeProvider` in Europe/Madrid, including a date equal to today and both dates past;
  - `OfferedWeaponModelIds` excludes models that are no longer rentable.

  Then implement them. Verify: the tests pass.
- [x] 3.10 Write a test that a weapon model offered in an edition cannot be deleted from the catalogue (`409`), and that an unused one still can. Then implement `FestivalEditionsCatalogUsage`. Verify: the tests pass.
- [x] 3.11 Regenerate `contracts/openapi.json`. Problem titles are localised per HTTP status in `SharedResources*.resx`, which already has every status used; the `editions.*` codes are translated by the UI (task 6.3). Verify: `ResourceCompletenessTests` passes, and the OpenAPI diff is additive only.
- [x] 3.12 Review group 3 in parallel with `csharp-reviewer`, `security-reviewer` (authorization of every route, BR-10, BR-12) and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 4. Registry lock (backend)

- [x] 4.1 Add `registry_settings` (design D8) with a migration that inserts the unlocked row. Write tests for `GET /api/registry/lock` (every signed-in user) and `PUT /api/registry/lock`:
  - Admin locks and unlocks, audited `RegistryLocked` / `RegistryUnlocked`;
  - repeating the same state is not audited;
  - FiringChief `403`.

  Then implement them. Verify: the tests pass.
- [x] 4.2 Write `RegistryLockGuardTests`. With the registry locked, every FiringChief write must answer `409 registry.locked` and change nothing: register, update (including status, license and course), delete, the three owned-weapon writes, and the photo upload and removal for each kind. No image may be stored. With the registry locked, Admin register, update, transfer, delete and import must succeed. A route-table test fails for any mutating `/arquebusiers` route the class does not list. Then add `RegistryOutcome.RegistryLocked`, `EnsureWritableAsync`, the early check for photo uploads, and the `registry.locked` code. Verify: the tests pass, and the existing registry tests pass unchanged.
- [x] 4.3 Write a concurrency test: a FiringChief edit holds its transaction while an Admin locks. The lock waits, the edit commits, and the next FiringChief edit is refused. Verify: the test passes reliably over 20 runs.
- [x] 4.4 Regenerate `contracts/openapi.json`. Document the lock in `backend/src/Modules/README.md`, in a "Conventions shared by modules (from `add-festival-editions`)" section: the lock check in registry writes, money as `decimal`, and the single-in-progress index. Verify: the OpenAPI diff is additive, and the README names `EnsureWritableAsync`.
- [x] 4.5 Review group 4 in parallel with `csharp-reviewer`, `security-reviewer`, `database-reviewer` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 5. Synthetic seed

- [x] 5.1 Write tests for `EditionSeeder` (design D11):
  - the past `CLOSED` edition, the `IN_PROGRESS` current edition with open orders, every date and price, rentable models of several kinds and milestones, and the next-year `DRAFT`;
  - the order window of the current edition includes the seed date;
  - running it twice creates nothing new;
  - it refuses to run outside Development/Testing.

  Then implement it. Verify: the tests pass, and `docker compose run --rm api-seed` succeeds twice.
- [x] 5.2 Document the seeded editions and the unlocked registry in `docs/development.md` ("Synthetic seed data"), including resetting the database for fresh dates. Verify: the section matches the seeder.

## 6. Frontend foundations

- [x] 6.1 Write tests for `MoneyInput`:
  - typing "4,50" in es-ES and ca-ES-valencia and "4.50" in en gives 4.5;
  - "4,555" is invalid;
  - the label and the `€` suffix are announced;
  - errors are linked with `aria-describedby`;
  - axe passes.

  Then implement it, with its story. Add `currency` to `useFormatters`, with tests for the three locales. Document `MoneyInput` in `docs/design/README.md`. Verify: the tests pass, and `npm run docs:design` is up to date.
- [x] 6.2 Replace the `edition` mapping in `components/app/status.ts` with `DRAFT`, `IN_PROGRESS` and `CLOSED`, and add the `orders` mapping (`OPEN`, `CLOSED`) (design D10), with labels in the three locales. Verify: the status tests pass, and `docs/design/status.md`, regenerated with `npm run docs:design`, shows both mappings.
- [x] 6.3 Add the `editions` namespace (three locales) to `i18n/index.ts`, `common:nav.editions`, and the navigation entry (design D10). Add the routes with their breadcrumbs, and `problems.ts` with every `editions.*` code and field reason. Verify: `npm run check-i18n` and `npm run typecheck` pass, and the navigation test lists Editions for both roles.

## 7. Editions screens

- [x] 7.1 Write tests for `EditionListPage` with MSW:
  - the rows with year, festival dates, status badge, the orders badge for the edition in progress, and the current marker;
  - "New edition" for Admins only;
  - the Admin and FiringChief empty states;
  - the load failure with retry;
  - axe passes, and the layout works at 360 px.

  Then implement it. Verify: the tests pass.
- [x] 7.2 Write tests for `EditionCreatePage`:
  - the Zod rules, mirroring the server;
  - the note naming the edition whose prices and models will be copied, or that nothing will be copied;
  - `409 editions.yearTaken` on the year field;
  - success opens the detail.

  Then implement it. Verify: the tests pass.
- [x] 7.3 Write tests for `EditionDetailPage` read mode:
  - the header with status and orders badges;
  - the key facts (festival dates, next order window date in words);
  - the four sections;
  - prices as currency in each locale;
  - offered models with the no-longer-rentable mark;
  - FiringChiefs see no edit, status or orders actions;
  - the not-found page for a `404`.

  Then implement it. Verify: the tests pass.
- [x] 7.4 Write tests for the section edit panels (dates and order window, prices, offered models, milestones):
  - each saves only its section with the `version`;
  - `SaveNotice` is announced and focus returns to the trigger;
  - `409 editions.modified` keeps the panel open and reloads;
  - field errors from the server land on their fields;
  - the models panel offers active rentable models by kind, and allows only unchecking a model that is no longer rentable;
  - milestone add, edit and remove with confirmation.

  Then implement them. Verify: the tests pass.
- [x] 7.5 Write tests for `EditionActions`, following the design D10 table:
  - "Start edition" and "Delete edition" for a `DRAFT`;
  - "Open orders" or "Close orders" as the primary action for the edition in progress;
  - "Close edition" and "Back to preparation", disabled with a hint while orders are open;
  - "Reopen edition" for a `CLOSED` edition;
  - each `ConfirmDialog` text saying what changes for FiringChiefs;
  - `incomplete` lists the missing fields in words in the dialog;
  - `anotherInProgress` names the year;
  - success updates the badges and announces it.

  Then implement them. Verify: the tests pass.
- [x] 7.6 Write tests for `CurrentEditionCard` on `DashboardPage`:
  - year, orders open or closed, the next order window date and up to three upcoming milestones, with a link;
  - "no edition in progress", with the editions link for Admins only;
  - a failure of the card keeps the alerts dashboard visible;
  - the existing dashboard tests pass unchanged.

  Then implement it. Verify: the tests pass.
- [x] 7.7 Describe the edition-actions pattern in `docs/design/patterns.md`: the current state's main action as the primary button, the rarer moves in "More actions", and a confirmation saying what changes for FiringChiefs. Verify: the pattern names the composites it uses.
- [x] 7.8 Review group 7 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 8. Registry lock screens

- [x] 8.1 Write tests for the lock UI in `features/arquebusier-registry`:
  - the Admin "Lock registry" / "Unlock registry" action with confirmation;
  - the Admin and FiringChief notices on the list and detail pages;
  - FiringChiefs see no register, edit, status, delete, owned-weapon or photo action while the registry is locked;
  - a `registry.locked` refusal in an edit panel shows the reason and switches the page to the locked state;
  - axe passes.

  Then implement them, with the `registry:lock.*` keys in three locales. Verify: the tests pass, and `npm run check-i18n` passes.
- [x] 8.2 Review group 8 in parallel with `react-reviewer`, `a11y-architect` and `security-reviewer` (to confirm the UI only hides actions and the server decides). Fix CRITICAL/HIGH findings.

## 9. End-to-end, documentation and verification

- [x] 9.1 Add the `serial-state` Playwright project, which depends on the main projects (design D11). Write `e2e/editions.spec.ts`:
  - an Admin creates a draft for a far year, sees the copied prices, sets the order window and a milestone, tries "Start edition" and gets the "another edition in progress" message, then deletes the draft;
  - the seeded FiringChief sees the current edition read-only on the editions page and on the start page, and gets the not-found page for the seeded draft;
  - axe runs on the list and detail pages.

  Verify: the spec passes in the compose stack.
- [x] 9.2 Write `e2e/serial-state/registry-lock.spec.ts` and `e2e/serial-state/edition-orders.spec.ts`. Each restores the state it changed in `afterEach`:
  - an Admin locks the registry; the FiringChief sees the notice and no edit actions; the Admin edits an arquebusier; the Admin unlocks it;
  - an Admin closes the orders of the current edition and opens them again; the FiringChief's start page shows "Orders closed", then "Orders open".

  Verify: both pass, and the full suite passes twice in a row.
- [x] 9.3 Update the docs:
  - `docs/data-model.md`: the edition fields; `status` `DRAFT` / `IN_PROGRESS` / `CLOSED` and the `ordersOpen` flag, replacing the five statuses; the order window as a published plan; the single edition in progress; prices as euros with two decimals; `CalendarMilestone` without `notify` until #14; the registry lock; the BR-10 wording ("while the orders of the current edition are open");
  - `docs/use-cases.md`: UC-10 and UC-11 notes;
  - `docs/glossary.md`: `EditionStatus`, "orders open" (`ordersOpen`), "current edition" (`currentEdition`) and "registry lock" (`RegistryLock`);
  - `docs/open-questions.md`: Q-25 note, that a corrections window is the orders reopened;
  - `docs/compliance.md`: SEC-05, noting that edition changes, orders opening and closing, and lock toggles are audited;
  - `docs/mvp.md`: the status of #9, set when the change is archived.

  Verify: the documents match the specs, no document still names `ORDERS_OPEN`, `CORRECTIONS_OPEN` or `LOCKED` as edition statuses, and no real dates, prices or names from `docs/sources/` appear.
- [x] 9.4 Run `verification-loop`: build, types, lint, backend and frontend tests with coverage of at least 80 % on the new module and the lock code, a security grep (no unauthenticated write route, no personal data in audit entries), and a diff review. Run `e2e-runner` and `pr-test-analyzer` on the change. Verify: the PASS report, with the findings and follow-ups recorded in design.md.
