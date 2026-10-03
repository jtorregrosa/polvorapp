# Tasks

## 1. Research

- [x] 1.1 Check with Context7 (and `gh search code` for real-world usage) the APIs used in design D2–D4, D9 and D12:
  - Npgsql / ADO.NET: running a command on an existing `DbTransaction`'s connection from another module's code (`transaction.Connection.CreateCommand()`, `command.Transaction`), and `FOR SHARE` under READ COMMITTED;
  - EF Core 10 / Npgsql: a partial unique index filtered on `IS NOT NULL`, check constraints over coded enums, raw-SQL foreign keys with `ON DELETE SET NULL`, and reading a constraint's delete rule from `information_schema.referential_constraints` in a test;
  - React Hook Form + Zod: a discriminated union for the weapon source and the loan (registered or external owner), and resetting dependent fields when the status becomes `RESERVE`;
  - the shadcn/ui `textarea` primitive and its accessible counter pattern.

  Also find where the BR-01 DNI/NIE validator lives (SharedKernel or the registry) and how orders can reuse it without referencing the registry implementation. Record versions, workarounds and the validator decision in design.md. Verify: design.md updated, with no open question left.

## 2. Module skeleton and data model

- [x] 2.1 Create `PolvorApp.ComparsaOrders` and `.Contracts` (design D1): `ComparsaOrdersModule` (`MigrationOrder = 50`), `ComparsaOrdersDbContext` (schema `orders`, `AddAuditTrail`), the design-time factory, `OrderWriteGuard`, and the coded enums `OrderStatus`, `EntryStatus` (reuse the registry's `ArquebusierStatus` codes), `CapsType`, `WeaponSource`, `FlaskOption` and `LenderKind`. Register the module in `Program.cs`, `PolvorApp.slnx`, the `Dockerfile` and `ModelDriftTests`. Verify: `dotnet build` passes, and the architecture tests, `CodedEnumsTests` and `ModelDriftTests` pass.
- [x] 2.2 Add the entities `ComparsaOrder`, `EditionEntry` and `WeaponLoan` with their `xmin` versions, the check constraints, the indexes `ux_comparsa_orders_edition_comparsa` and `ux_edition_entries_edition_arquebusier`, and the initial migration with the raw-SQL foreign keys of design D2. Write Testcontainers tests:
  - a second order for the same comparsa and edition violates the unique index;
  - a second entry for one arquebusier in one edition violates the partial index, while two entries no longer linked to the registry do not;
  - a `RESERVE` entry with powder, caps without a type, a rental without a model, an owned weapon copy on another source, and an external loan naming a lender comparsa each violate a check;
  - deleting an arquebusier nulls `arquebusier_id` on their entries and the weapon ids on entries and loans, keeps every copy column, and the deletion succeeds;
  - deleting an edition, a comparsa or a weapon model referenced by orders violates its foreign key;
  - every cross-schema key exists with the delete rule of D2.

  Verify: the tests pass, and `migrate` applies the migration to a fresh database.
- [x] 2.3 Document the `ON DELETE SET NULL` convention in `backend/src/Modules/README.md`: when a reference must outlive its target, the copy the referencing module keeps, and how null reads as "no longer there". Update the comment of `ArquebusierAdministration.DeleteAsync` (entries are kept). Add `ComparsaOrder`, `EditionEntry` and `WeaponLoan` to `docs/data-model.md`: the statuses, the entry fields without `rentalWeapon` and `rentalFlaskNumber` until #13, the external owner, and the history copy (design D3). Also amend BR-05 (a `RESERVE` entry has no powder, caps, weapon or flask; an `ACTIVE` entry may have no powder or no weapon), BR-09 (an external owner is allowed) and BR-14 (entries are kept as history with their copy, and anonymised only on a GDPR request). Verify: the docs match the specs, and no real data from `docs/sources/` appears.
- [x] 2.4 Review group 2 in parallel with `csharp-reviewer` and `database-reviewer`. Fix CRITICAL/HIGH findings.

## 3. Contracts in other modules

- [x] 3.1 Write an integration test in which an order-style write holds `IEditionDirectory.ReadForOrderWriteAsync` on its own transaction while `SetOrdersAsync` closes the orders. The close waits, then commits, and a write that starts afterwards reads `OrdersOpen = false`. Then implement `ReadForOrderWriteAsync` (design D4) and fix the doc comment of `FestivalStartsOn`. Verify: the test passes, and the existing `EditionRaceTests` pass.
- [x] 3.2 Write endpoint tests for deleting a `DRAFT` edition with orders: `409 editions.inUse` through the `IEditionUsage` veto, and through the foreign key when a fake veto answers false. Then add `IEditionUsage` to `FestivalEditions.Contracts`, ask it in `EditionAdministration.DeleteAsync` after the `FOR UPDATE`, and map the foreign key violation. Add `editions:errors.inUse` in three locales and the delete dialog's handling. Verify: the tests pass, and `npm run check-i18n` passes.
- [x] 3.3 Write tests for `IArquebusierRoster` (design D5): the comparsa roster sorted in Spanish order with facts and owned weapons; `FindManyAsync`; `FindLenderAsync` by normalised `nationalId`, with names, national ID, comparsa and weapons, and no birth date or contact; `FindOwnedWeaponsAsync` with the ownership guide (server-side only); and `IsNationalIdRegisteredAsync`. Then implement it in the registry, and add `ArquebusierId` to `ArquebusierFacts`. Verify: the tests pass, and the compliance tests pass unchanged.
- [x] 3.4 Write unit tests for `IComplianceRules.EvaluateForFestival`, covering every scenario of "Compliance warnings of edition entries (BR-04)":
  - a license expiring during the festival;
  - a license valid through `festivalEndsOn`;
  - age boundaries on `festivalStartsOn`;
  - no `LICENSE_EXPIRING`;
  - course and photos.

  Then implement it, sharing the rule functions with `Evaluate`. Verify: the tests pass, and the existing compliance tests pass unchanged.
- [x] 3.5 Implement `OrdersCatalogUsage : ICatalogUsage` (comparsas with orders, models used by rentals or external loans). Test it with the catalogue's delete endpoints (`409` while in use). Verify: the tests pass.
- [x] 3.6 Write registry tests with a fake participant:
  - `DeleteAsync` calls every `IArquebusierDeletionParticipant` on its own transaction after locking the arquebusier;
  - a participant failure rolls the whole deletion back as `registry.busy`;
  - the returned effect is recorded in `ArquebusierDeleted` with ids only.

  Then add the contract to `ArquebusierRegistry.Contracts` (design D3, D5), call it from `DeleteAsync`, and update its comment. Verify: the tests pass, and the existing deletion tests pass unchanged.
- [x] 3.7 Review group 3 in parallel with `csharp-reviewer`, `database-reviewer` and `security-reviewer` (the roster must expose nothing beyond D5). Fix CRITICAL/HIGH findings.

## 4. Preparation, pre-fill and entries

- [x] 4.1 Write unit tests for `Prefill.For`, covering every scenario of "Pre-fill from the previous edition":
  - copy from an `ACTIVE` previous entry;
  - an owned weapon no longer owned;
  - a rental model no longer offered;
  - loans never copied;
  - a `RESERVE` previous entry;
  - no previous entry, with one owned weapon and with several;
  - registry `RESERVE`.

  Then implement it. Verify: the tests pass.
- [x] 4.2 Write unit tests for `EntryInput.Read` and `OrderStatusMoves.Check`:
  - every blocking rule of "Edition entries (BR-05, BR-07)" with field keys and reasons;
  - every pair of order statuses for each role, where only the moves in design D8 pass (an Admin submits and validates from `DRAFT` and `RETURNED` too).

  Then implement them. Verify: the tests pass.
- [x] 4.3 Write endpoint tests for `POST /api/comparsa-orders` and `GET /api/comparsa-orders/{id}`:
  - `201` with one pre-filled entry per arquebusier;
  - the previous edition's values;
  - arquebusiers with an entry in another order are left out;
  - `orders.alreadyPrepared` under two concurrent requests;
  - `orders.closed` for a FiringChief and success for an Admin;
  - `orders.editionNotStarted`, `orders.comparsaInactive`;
  - `404` out of scope and for a draft edition;
  - entries sorted by name in Spanish order;
  - each entry holds the history copy of identity and owned weapon.

  Then implement the preparation (design D8) and the order read. Verify: the tests pass.
- [x] 4.4 Write endpoint tests for adding, editing and removing entries:
  - add an arquebusier not in the order, and `orders.alreadyInEdition`;
  - edit with each weapon source and flask, and each `400` of the entry rules;
  - `entries.modified` on an outdated version;
  - a FiringChief edit of a `SUBMITTED` order returns it to `DRAFT`, and a `RETURNED` one keeps its status;
  - `orders.validated` for a FiringChief and success for an Admin, without a status change;
  - `orders.closed`;
  - every save refreshes the history copy from the registry (a corrected last name is copied);
  - the entry of an arquebusier no longer in the registry can still be set to `RESERVE`;
  - `ACTIVE` with 0 kg and an owned weapon, and with 2 kg and no weapon, are saved without issues;
  - no route deletes an entry or an order;
  - an unchanged save is not audited.

  Then implement `EntryAdministration`. Verify: the tests pass.
- [x] 4.5 Add the UC-12 notes (preparation, pre-fill, adding arquebusiers by hand, the entry status independent of the registry, `ACTIVE` without powder or weapon, entries never removed) to `docs/use-cases.md`. Verify: the notes match the spec.
- [x] 4.6 Review group 4 in parallel with `csharp-reviewer`, `security-reviewer` (scope and BR-10 on every write) and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 5. Weapon loans and the lender lookup

- [x] 5.1 Write endpoint tests for `POST /api/comparsa-orders/lender-lookup`:
  - a registered lender of another comparsa with names, comparsa and weapons, and no ownership guide;
  - not registered;
  - `400` for an invalid DNI with nothing recorded;
  - one `LoanLenderLookedUp` entry without the identifier;
  - `429` beyond the rate limit;
  - the DNI is accepted only in the body.

  Then implement it (design D9), and widen the `PersonalDataWrites` summary. Verify: the tests pass.
- [x] 5.2 Write endpoint tests for loans on the entry edit:
  - a registered owner of another comparsa;
  - `ownWeapon`;
  - an external owner with the guide upper-cased;
  - `loan.nationalId` invalid and `lenderRegistered`;
  - the same weapon lent twice;
  - a registered owner's loan keeps a copy of the lender and the weapon, refreshed on save;
  - leaving or replacing a loan in an editable order deletes its row;
  - the audit entry names the fields without the external values.

  Then implement the loans. Verify: the tests pass.
- [x] 5.3 Write endpoint tests for the lent-out list and loan visibility (BR-12):
  - the lender's FiringChief sees the weapon, the borrower's name and comparsa, and nothing else of that order;
  - the borrower's FiringChief sees the lender's name and comparsa, or the external owner.

  Then implement them. Verify: the tests pass.
- [x] 5.4 Update `docs/compliance.md`: the external owners in the personal data inventory (name, DNI/NIE, weapon ownership), their retention with the loan (SEC-08), the lookup (SEC-03, SEC-05), and the privacy notice note (Q-50). Update `docs/glossary.md`: `WeaponLoan` with the external owner, and "lender lookup". Verify: the docs match the specs.
- [x] 5.5 Review group 5 in parallel with `csharp-reviewer` and `security-reviewer` (enumeration, minimisation, logs without DNI). Fix CRITICAL/HIGH findings.

## 6. Submission, review, totals and registry effects

- [x] 6.1 Write unit tests for `EntryIssues.Of`: `ownedWeaponMissing` only while the arquebusier is in the registry, `loanWeaponMissing`, `rentalModelNotOffered`, and no issue for `ACTIVE` without powder or weapon. Then implement it. Verify: the tests pass.
- [x] 6.2 Write endpoint tests for submitting:
  - success with warnings, recording the user, the time and the attestation;
  - an Admin submits a `DRAFT` order with closed orders and without attestation, recorded as submitted by an Admin;
  - submission refreshes the copies of every entry and loan;
  - `attestation` missing;
  - `orders.invalidTransition`, `orders.closed`, `orders.modified`;
  - `orders.entriesInvalid` listing entries and reasons after an owned weapon is removed from the registry;
  - resubmitting a `RETURNED` order;
  - a FiringChief with closed orders gets `orders.closed`.

  Then implement the submission. Verify: the tests pass.
- [x] 6.3 Write endpoint tests for validating and returning:
  - validate a `SUBMITTED` order, and a `DRAFT` and a `RETURNED` order never submitted;
  - validation refreshes the copies;
  - return a `SUBMITTED` and a `VALIDATED` order with a reason;
  - `returnReason` empty or over 500 characters;
  - `orders.invalidTransition` when returning a `DRAFT` order or validating a `VALIDATED` one;
  - `orders.entriesInvalid` on validation;
  - reviews with closed orders;
  - a FiringChief gets `403`;
  - audit entries without the reason text.

  Then implement them. Verify: the tests pass.
- [x] 6.4 Write tests for the registry effects (spec "Entries after registry changes"):
  - a transfer keeps the entry in the previous comparsa's order;
  - with the orders open, a deletion removes the entry of the edition in progress with its loan, in `DRAFT`, `SUBMITTED` and `VALIDATED` orders. The order keeps its status, its version changes, its totals drop the entry, and the audit records the entry and order ids;
  - with the orders closed, a deletion keeps the entry of the edition in progress with its copy, and an Admin can still edit it;
  - a deletion keeps the entries of other editions with their copy and their totals, shown as no longer in the registry, without warnings or first-year flag;
  - a deletion that races with the closing of the orders serialises on the edition row;
  - a deletion that races with an order write on the same order serialises on the order lock, and the remaining cycle (writer holding the order, registry holding the arquebusier) ends as a retryable 503 on one side and commits on the other;
  - an order write that loses a race with a registry deletion (`23503` on a registry key) answers `409 orders.modified`;
  - a deleted lender marks the borrower's loan as weapon removed, keeping its copy;
  - a registry status change leaves the entries unchanged.

  Then implement the orders `IArquebusierDeletionParticipant` (design D3). Verify: the tests pass, and the registry deletion tests pass.
- [x] 6.5 Write unit tests for `OrderTotals.Of`, covering every total of "Order totals and dashboard (UC-16)", and endpoint tests for `GET /api/comparsa-orders/overview`:
  - the Admin rows include not-prepared active comparsas, the status counts and the edition totals;
  - FiringChief rows are only their scope, without Federation totals;
  - the current edition when `editionId` is omitted, and `{ edition: null }` without one;
  - `404` for a draft edition for a FiringChief;
  - an 800-entry edition answers well within NFR-05.

  Then implement the overview. Verify: the tests pass.
- [x] 6.6 Add the UC-13 to UC-16 notes to `docs/use-cases.md`: submit and resubmit, the edit returning to `DRAFT`, validated orders read-only for FiringChiefs, Admin submission on the comparsa's behalf, Admin validation of an order never submitted, Admin edits keeping the status, and the dashboard. In `docs/compliance.md`, add SEC-05 (the order actions that are audited). Add SEC-08 and the inventory entry for the order history, which removes the current-edition entry on deletion and keeps the identity and weapon data of deleted arquebusiers in past editions until a GDPR request (UC-26), with the legal-basis note for the Federation (Q-50). Verify: the docs match the specs.
- [x] 6.7 Review group 6 in parallel with `csharp-reviewer`, `database-reviewer`, `security-reviewer` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 7. First year

- [x] 7.1 Write tests for `IParticipationHistory.FirstYearAsync` and `GetDeletionImpactAsync`:
  - unknown without an earlier order;
  - a `RESERVE` earlier entry still counts as first year;
  - an entry no longer linked to the registry is ignored;
  - the deletion impact gives the current entry's year, comparsa and order status, whether it will be removed (orders open), the lent weapons in the edition in progress, and whether past entries exist.

  Then implement them (design D5). Verify: the tests pass.
- [x] 7.2 Write endpoint tests in which the arquebusier detail returns `firstYear` (null without a current edition or history) and `deletionImpact`, and the order entries return `firstYear`. Then implement them. Verify: the tests pass, and `RegistryScopeGuardTests` pass.
- [x] 7.3 Write tests in which the statistics return the first-year counts by gender, or "unknown" without history or a current edition, and still contain no identifier. Then implement them in `ComplianceInsights`. Verify: the tests pass.
- [x] 7.4 Update `docs/data-model.md` §5 ("first year" derived on an edition, known only with history) and add "first year" and "attestation" to `docs/glossary.md`. Verify: the docs match the spec.
- [x] 7.5 Review group 7 in parallel with `csharp-reviewer` and `security-reviewer` (statistics stay aggregate). Fix CRITICAL/HIGH findings.

## 8. Synthetic seed

- [x] 8.1 Write tests for `OrderSeeder` (design D13):
  - running twice creates everything once;
  - the past edition's `VALIDATED` orders make the first-year flag known;
  - the current edition has Norte `SUBMITTED`, Sur `DRAFT` and Este not prepared;
  - every weapon source, flask, caps type and `RESERVE` is present, and `ACTIVE` entries without powder and without a weapon;
  - a past-edition entry with no registry link keeps its synthetic copy;
  - the cross-comparsa loan and the external loan with a DNI that passes BR-01 exist;
  - no seeded value appears in `docs/sources/` (the existing hygiene test).

  Then implement it. Verify: the tests pass, and `api-seed` runs twice in the compose stack.
- [x] 8.2 Update `docs/development.md` (what the seed creates for orders). Verify: the document matches the seeder.

## 9. Frontend foundations

- [x] 9.1 Write tests for the `TextArea` composite (label, description, error, counter announced politely, `maxLength`, axe in both themes) and its story. Then implement it over the `textarea` primitive. Document it in `docs/design/` (README component list). Verify: the tests, the Storybook catalogue test and the lint guardrails pass.
- [x] 9.2 Add the `orders` i18n namespace skeleton in three locales, the `common:nav.orders` key, the `orders` navigation item for both roles, and the routes `/orders`, `/editions/:editionId/orders` and `/orders/:orderId` with their breadcrumbs. Add `queries.ts`, `problems.ts` (every code of design D6), `entrySchema.ts` (Zod mirroring `EntryInput`, with tests for each rule) and `test-data.ts`. Verify: the schema tests pass, `npm run typecheck` and `npm run check-i18n` pass, and the navigation test lists "Orders" for both roles.
- [x] 9.3 Review group 9 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 10. Orders overview and order page

- [x] 10.1 Write tests for `OrdersOverviewPage`:
  - the Admin dashboard with status figures, edition totals by model and the comparsa table;
  - the FiringChief list with "Prepare order";
  - a FiringChief with one prepared order is redirected;
  - no current edition;
  - orders open or closed;
  - load failure with retry;
  - a 360 px layout;
  - axe.

  Then implement the page. Verify: the tests pass.
- [x] 10.2 Write tests for `OrderPage`:
  - the header with logo, statuses and why the order is read-only;
  - the key-fact totals;
  - the return reason, the warnings banner and the entries-to-check banner;
  - the entries table with first-year flag, warnings and issues, and its `mobileRow`;
  - entries no longer in the registry are marked, and no row has a remove action;
  - the "submitted by an Admin" note;
  - "Not in the order" with "Add";
  - "Lent to others";
  - `404` shows the not-found page;
  - axe.

  Then implement it. Verify: the tests pass.
- [x] 10.3 Add the "Orders" link to the edition detail for non-draft editions, with a test. Add an "Order page" pattern to `docs/design/patterns.md`. Fill in the `orders` translations used so far in es-ES, ca-ES-valencia and en. Verify: the tests pass, and `npm run check-i18n` passes.
- [x] 10.4 Review group 10 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 11. Entry panel and loans

- [x] 11.1 Write tests for `EntryEditSheet`:
  - status and powder as radio cards;
  - choosing `RESERVE` clears and disables the dependent fields;
  - caps type required with boxes;
  - weapon choices of owned weapons, offered models and loan;
  - client validation matching the server;
  - a server `400` mapped to fields;
  - `entries.modified` keeps the panel open and reloads;
  - `orders.closed` closes the panel and shows the order read-only;
  - "Changes saved" announced;
  - the order's return to `DRAFT` explained;
  - a bottom sheet on phones;
  - axe.

  Then implement it. Verify: the tests pass.
- [x] 11.2 Write tests for `LoanFields`:
  - the DNI is checked before the lookup;
  - a registered owner shows the weapons to choose from;
  - not registered shows the external owner and weapon fields;
  - a `429` message;
  - `lenderRegistered` and `ownWeapon` messages;
  - the DNI is never put in the address.

  Then implement them. Verify: the tests pass, and `npm run check-i18n` passes.
- [x] 11.3 Review group 11 in parallel with `react-reviewer`, `a11y-architect` and `security-reviewer` (no personal data in URLs, logs or caches). Fix CRITICAL/HIGH findings.

## 12. Submission and review screens

- [x] 12.1 Write tests for `SubmitDialog`:
  - it lists the pending warnings in words;
  - for a FiringChief, "Submit" is enabled only with the attestation;
  - for an Admin, the dialog says it submits on the comparsa's behalf and has no attestation;
  - `orders.entriesInvalid` lists the entries to fix with links;
  - `orders.modified` reloads.

  Then implement it. Verify: the tests pass.
- [x] 12.2 Write tests for the Admin "Submit on behalf of the comparsa", "Validate" and "Return" actions:
  - "Validate" on a `DRAFT` or `RETURNED` order says that the comparsa did not submit it;
  - the return dialog requires a reason, with a counter;
  - each transition is announced;
  - `orders.invalidTransition` and `orders.entriesInvalid` messages.

  Then implement them. Verify: the tests pass, and `npm run check-i18n` passes.
- [x] 12.3 Review group 12 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 13. Registry, statistics and editions screens

- [x] 13.1 Write tests in which the arquebusier detail shows the "First year" badge when `firstYear` is true and nothing when it is null, and that the delete confirmation shows `deletionImpact`:
  - the warning that names the edition, the comparsa and the order status of the entry that will be deleted while the orders are open, or the note that it stays as history when they are closed;
  - the lent weapons that will show as removed;
  - that past entries are kept;
  - no warning when there is no impact;
  - cancelling leaves everything unchanged. Then implement them, with the `registry:*` keys in three locales. Verify: the tests pass, and `npm run check-i18n` passes.
- [x] 13.2 Write tests in which the statistics page shows the first-year table by gender with shares, or the "available once an earlier edition has orders" message. Then implement them, with the `insights:*` keys in three locales. Verify: the tests pass, axe passes, and `npm run check-i18n` passes.
- [x] 13.3 Review group 13 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 14. End-to-end and verification

- [x] 14.1 Write `e2e/orders.spec.ts`, which uses the seeded data read-only:
  - an Admin sees the dashboard of the current edition with `SUBMITTED`, `DRAFT` and not prepared;
  - a FiringChief opens their order, sees the first-year flags, the warnings and the loan lent out, and opens a past edition's order read-only;
  - axe runs on the overview and the order page.

  Verify: the spec passes in the compose stack.
- [x] 14.2 Write `e2e/serial-state/order-review.spec.ts` (design D13):
  - the FiringChief prepares the order if needed, sets an entry to `RESERVE`, registers a loan from an external owner, and submits with the attestation;
  - the Admin returns it with a reason;
  - the FiringChief sees the reason, fixes the entry and resubmits;
  - the Admin validates;
  - `afterEach` returns the order;
  - the FiringChief registers a synthetic arquebusier, adds them to the current order, and opens the delete confirmation. It warns that their entry will be deleted from that order. After confirming, the entry is gone from the order.

  Also check on a 360 px viewport that the entry panel opens as a bottom sheet. Verify: the spec passes, and the full suite passes twice in a row.
- [x] 14.3 Run `verification-loop`:
  - build, types and lint;
  - backend and frontend tests with coverage of at least 80 % on the new module and the changed contracts;
  - a security grep: no unauthenticated write route, no DNI in routes or logs, no personal data in audit entries;
  - a diff review.

  Run `e2e-runner` and `pr-test-analyzer` on the change. Verify: the PASS report, with the findings and follow-ups recorded in design.md. `docs/mvp.md` marks #10 done when the change is archived.
