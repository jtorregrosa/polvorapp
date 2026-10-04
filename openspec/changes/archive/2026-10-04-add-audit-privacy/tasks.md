# Tasks

## 1. Research

- [x] 1.1 Check with Context7, and with `gh search code` for real-world usage, what designs D3, D4, D10 and D11 rely on:
  - PostgreSQL `set_config(..., true)` and `current_setting(..., true)` inside an Npgsql transaction;
  - `BEFORE TRUNCATE` statement triggers, and row-value `IS DISTINCT FROM`;
  - EF Core keyset paging with a row-value comparison on `(occurred_at, id)` through Npgsql (`EF.Functions.LessThan` on tuples, or raw SQL);
  - batched `ExecuteDeleteAsync` with `Take`;
  - `System.IO.Compression.ZipArchive` in memory;
  - ClosedXML with several sheets;
  - TanStack Query v5 `useInfiniteQuery` with Orval's generated hooks.

  Record the versions and any workaround in a "Research notes" section of design.md. Verify: the section exists and leaves no open question beyond Q-50.

## 2. Database guard, retention and catalogue

- [x] 2.1 Write database tests for the `audit` migration (design D4, D9, D10):
  - `UPDATE`, `DELETE` and `TRUNCATE` are refused without the maintenance setting;
  - `purge` mode deletes rows and cannot update them;
  - `redact` mode changes `data` only, and refuses changes to any other column and deletes;
  - existing `UserUpdated` rows lose `previous.name` and `current.name`;
  - the composite index replaces the `occurred_at` index.

  Then write the migration and `AuditMaintenance`. Update the existing `Audit_entries_cannot_be_modified`/`deleted` tests so they also hit the trigger through raw SQL. Verify: the tests and `ModelDriftTests` pass.
- [x] 2.2 Update `AuditTrailRulesTests`:
  - allowlist only `Maintenance/AuditMaintenance.cs`;
  - add a rule that forbids `polvorapp.audit_maintenance` elsewhere in `src/`;
  - add a positive case and a negative case for each rule.

  Verify: the architecture tests pass, and a probe file outside the allowlist fails them.
- [x] 2.3 Write tests for `AuditActionCatalog` (design D2):
  - the union of the sources, with duplicates refused at start-up;
  - the `Security` class for the eight listed codes;
  - unknown codes default to `Standard`.

  Make recording an undeclared code a programming error in `AuditTrail` (design D2: a runtime check, enforced by the whole integration suite, replaces a static scan). Then add an `IAuditActionSource` to each module (IdentityAccess, ArquebusierRegistry, FederationCatalog, FestivalEditions, ComparsaOrders, Distribution, Exports, Notifications, AuditPrivacy), listing its existing codes. Verify: the tests pass and no existing test changes.
- [x] 2.4 Write tests for `UserUpdated` recording `changedFields: ["name"]` without name values, with role and locale still recorded as previous and current. Then change `UserAdministration`. Verify: the new test and the identity audit tests pass.
- [x] 2.5 Write tests for `AuditRetentionOptions`: the defaults, start-up refusing values below the minimum or not whole, and a message that names the setting only. Then write tests for `AuditPurge` with `FakeTimeProvider`:
  - the 365/366-day and 5-year ± 1-day edges;
  - `Security` and `Standard` codes;
  - batches of 5,000;
  - one `AuditEntriesPurged` entry with counts and none when nothing is deleted.

  Then implement them, together with `AuditRetentionService` and the `purge-audit` host command. Verify: the tests pass, and the service stays off in `ApiFactory` unless a test enables it.
- [x] 2.6 Update `docs/compliance.md` (SEC-05 retention and the trigger; go-live: a low-privilege runtime role) and `docs/development.md` (the `Audit__*` settings and `purge-audit`). Verify: the docs match design D3 and D4.
- [x] 2.7 Review group 2 in parallel with `csharp-reviewer`, `database-reviewer` and `security-reviewer`. Fix CRITICAL/HIGH findings.

## 3. Audit log API

- [x] 3.1 Write unit tests for the audit query:
  - cursor encoding and decoding, with a malformed cursor giving `400 cursor`;
  - filter validation (`from` after `to`, unknown `action` or `entityType`, `entityId` without `entityType`, `limit` outside 1–100, `actorUserId=none`);
  - Europe/Madrid day bounds on both DST transition days.

  Then implement `AuditQuery`. Verify: the tests pass.
- [x] 3.2 Add `IUserDirectory.GetManyAsync(ids)` with `ErasedAt` and a comparsa batch read in `ICatalogDirectory`, test first. Then add `IAuditRecordResolver` for `Arquebusier`, `User`, `Comparsa`, `WeaponModel`, `FestivalEdition` and `ComparsaOrder`, in their owning modules. Verify: the contract tests pass.
- [x] 3.3 Write endpoint tests for `GET /api/audit-entries` and `GET /api/audit-entries/actions`:
  - newest first, 50 by default;
  - a next page that neither skips nor repeats while new entries are written;
  - each filter on its own and combined;
  - actor and comparsa names;
  - "system" or "anonymous" for an entry without a user;
  - `recordExists` false after a deletion;
  - `403` for a FiringChief;
  - no audit entry written by reading.

  Then implement them. Verify: the tests and `OpenApiDocumentTests` pass.
- [x] 3.4 Review group 3 in parallel with `csharp-reviewer`, `database-reviewer` (query plans for each filter with `EXPLAIN` on 100,000 synthetic rows) and `type-design-analyzer`. Fix CRITICAL/HIGH findings.

## 4. Personal-data participants

- [x] 4.1 Create `IPersonalDataParticipant`, `PersonalDataSubject`, the summary, export and erasure part records and the `PolvorApp:PersonalData` model annotation in `AuditPrivacy.Contracts` and the SharedKernel (design D5). Annotate the personal columns of the registry, orders, identity, catalog and notifications models. Write the architecture or integration test that fails when an annotated table's module registers no participant. Verify: the test fails before any participant exists, then passes once group 4 is done.
- [x] 4.2 Write the tests for the orders migration (design D7): `erased_at` on entries and loans, the check constraints that refuse an erased row with copy values, and both partial indexes. Then write the migration. Verify: the tests and `ModelDriftTests` pass.
- [x] 4.3 Split `ArquebusierAdministration.DeleteAsync` into `DeleteCoreAsync(arquebusier, transaction)` with no behaviour change. Verify: every `ArquebusierDeletion*` test passes unchanged.
- [x] 4.4 Write tests for the registry participant:
  - describe and export the record, the weapons and the photos (photo bytes from MinIO);
  - erase through the deletion core: `ArquebusierDeleted` with `source: "gdprErasure"`, the current-edition entry removed while the orders are open, and the photo keys returned for after the commit;
  - nothing for an unknown DNI.

  Then implement it. Verify: the tests pass.
- [x] 4.5 Write tests for the orders participant:
  - entries found by copy `national_id` and by arquebusier id, including a DNI corrected in the registry;
  - loans found by `lender_national_id`, external and registered;
  - the export sheets without other people's identities;
  - anonymisation blanks exactly the copy columns and sets `erased_at`, keeping quantities, model and status.

  Then implement it. Verify: the tests pass.
- [x] 4.6 Write tests for the distribution participant: proxies where an erased entry is the holder or the proxy, exported by role only and removed on erasure. Then implement it. Verify: the tests pass.
- [x] 4.7 Write tests for the user participants (design D8):
  - identity: the profile export has no secrets; erasure sets the marker name, the `.invalid` email and `ErasedAt`, removes the password, tokens, claims and logins, ends sessions, and refuses self-erasure and a second erasure;
  - catalog: assignments are exported and removed;
  - notifications: opt-outs and deliveries are exported and removed;
  - audit: the activity is exported, and `SignInFailed` `attemptedEmail` is redacted.

  Add the identity migration (`erased_at`), the `ERASED` derived status and the `409 identity.userErased` refusals for edit, reactivate, resend and 2FA reset. Then implement them. Verify: the tests and every identity test pass.
- [x] 4.8 Write a regression test that runs every user-management and security action for a synthetic user and asserts that no audit `data` holds their name or email, apart from `SignInFailed.attemptedEmail`. Verify: the test passes.
- [x] 4.9 Update the modules README with the personal-data participant convention and the `PersonalData` annotation. Verify: the README matches the code.
- [x] 4.10 Review group 4 in parallel with `csharp-reviewer`, `database-reviewer`, `security-reviewer` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 5. GDPR requests API

- [x] 5.1 Add the `privacy` rate-limit policy and the problem codes (design D11). Write endpoint tests for `POST /api/privacy/people/lookup`:
  - a registered arquebusier, a former arquebusier, an external owner, and nothing held;
  - `warnings` for non-closed editions and open orders;
  - `400 nationalId` with nothing audited;
  - `PersonLookedUp` without DNI/NIE or name;
  - `429`;
  - `403` for a FiringChief.

  Then implement them. Verify: the tests pass.
- [x] 5.2 Add `IDocumentRenderer.RenderWorkbook(sheets)` in Exports, test first (several sheets, neutralised text cells). Then write tests for `PersonalDataWorkbook` and the ZIP: the sheets per category in the Admin's language, "About this data", `photos/{kind}.jpg`, the neutral file name and `no-store`. Then implement them. Verify: the tests pass.
- [x] 5.3 Write endpoint tests for `POST /api/privacy/people/export` and `POST /api/privacy/users/{id}/export`:
  - the contents for the spec scenarios;
  - `400 reference`;
  - `404`;
  - `503` with no file when the audit fails;
  - `PersonalDataExported` with the reference and counts and no identity;
  - `403` for a FiringChief.

  Then implement them. Verify: the tests pass.
- [x] 5.4 Write endpoint tests for `POST /api/privacy/people/erasure` (design D6):
  - each spec scenario;
  - totals and billing unchanged;
  - atomicity with a participant forced to fail (nothing changed, nothing audited);
  - an erasure racing an arquebusier deletion gives a single outcome;
  - `privacy.busy` on a lock timeout;
  - images erased after the commit, and left to the sweeper when that fails;
  - `404` for a second erasure;
  - `PersonalDataErased` in the transaction.

  Then implement the orchestration. Verify: the tests pass.
- [x] 5.5 Write endpoint tests for `POST /api/privacy/users/{id}/erasure`:
  - each spec scenario;
  - the erased user cannot sign in;
  - the audit log shows their earlier entries as "erased user";
  - `409` for self and already erased;
  - `404`;
  - `403` for a FiringChief.

  Then implement it. Verify: the tests pass.
- [x] 5.6 Regenerate `contracts/openapi.json`. Check `OpenApiDocumentTests`: no non-GET "audit" operation, no user deletion, and the new routes and problem codes. Verify: the tests pass and the diff is additive.
- [x] 5.7 Review group 5 in parallel with `csharp-reviewer`, `security-reviewer` (DNI/NIE handling, logs, file names, rate limits) and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 6. Erased entries in orders, exports and distribution

- [x] 6.1 Write tests for the orders changes:
  - `erased` in the order response;
  - `409 orders.entryErased` on editing an erased entry or a loan with an erased lender;
  - a source change deletes an erased loan;
  - totals, the orders dashboard and billing count erased entries;
  - the lender lookup and pre-fill skip them.

  Then implement them. Verify: the tests and every orders and billing test pass.
- [x] 6.2 Write tests for exports: per-person rows leave erased entries out, the totals stay the same, an erased lender shows the model with an empty lender, and the audit counts the rows written. Then implement them. Verify: the tests and every exports test pass.
- [x] 6.3 Write tests for distribution: `400 entryErased` for an erased holder or proxy, lists without erased entries, and numbering without gaps. Then implement them. Verify: the tests and every distribution test pass.
- [x] 6.4 Review group 6 in parallel with `csharp-reviewer` and `database-reviewer`. Fix CRITICAL/HIGH findings.

## 7. Screens

- [x] 7.1 Regenerate the API client. Add the `audit` and `privacy` namespaces in `es-ES`, `ca-ES-valencia` and `en`, with a label for every catalogue action and entity type, plus the `common`, `identity`, `orders` and `distribution` keys of design D12. Add a test that every code in the generated catalogue has a label in each locale. Verify: `npm run typecheck`, `npm run check-i18n` and the label test pass.
- [x] 7.2 Write tests for `apiDownloadPost` (body sent, file name from the header, problem codes mapped). Then implement it. Verify: the tests pass.
- [x] 7.3 Write tests (Vitest + Testing Library + MSW + axe) for `AuditLogPage`:
  - filters kept in the address, with the last 30 days by default;
  - "Show more" appends the next page;
  - links only for existing linkable records;
  - "System", "Anonymous" and "Erased user";
  - the details sheet with the data fields and the trace id;
  - the empty and error states;
  - "not allowed" for a FiringChief.

  Then implement it with the route and the navigation item. Verify: the tests and axe pass in both themes.
- [x] 7.4 Write tests for "View history" on the arquebusier and user detail pages (Admins only, the filtered address). Then implement it. Verify: the tests and the existing detail page tests pass.
- [x] 7.5 Write tests for `PrivacyRequestsPage`:
  - lookup with the DNI never in the address;
  - the summary and the "nothing held" result;
  - the download dialog with the reference and its help;
  - the erasure dialog with each warning, focus starting on Cancel, the verb "Erase", the outcome announced and the lookup cleared;
  - translated problem messages;
  - "not allowed" for a FiringChief.

  Then implement it with the route and the navigation item. Verify: the tests and axe pass.
- [x] 7.6 Write tests for the user detail page:
  - "Download personal data" and "Erase personal data" under "More actions";
  - the `ERASED` badge and name;
  - only "View history" for an erased user;
  - the `ERASED` filter in the users list.

  Then implement them. Verify: the tests and the existing user tests pass.
- [x] 7.7 Write tests for an erased entry on the order page (the "Erased person" row without edit actions) and for the distribution proxy pickers skipping erased entries. Then implement them. Verify: the tests and the existing orders and distribution tests pass.
- [x] 7.8 Update `docs/design/patterns.md` with the audit log page (server paging, details sheet) and GDPR requests (reference field, irreversible confirmation with warnings). Verify: the notes match the screens.
- [x] 7.9 Review group 7 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 8. Documentation

- [x] 8.1 Update the docs:
  - `docs/data-model.md`: `erasedAt` on entries, loans and users, `ERASED`, the audit retention and trigger, and BR-14 pointing to the erasure;
  - `docs/glossary.md`: GDPR request, erasure, request reference, erased entry and erased user;
  - `docs/use-cases.md`: UC-25 and UC-26 notes with the maintainer decisions;
  - `docs/compliance.md`: SEC-08 and SEC-09 implemented, the privacy notice to state the retention and backups;
  - `docs/open-questions.md`: the four maintainer decisions dated 2026-10-04 (erasure never blocked with a warning; arquebusiers, external owners and users; a ZIP with Excel and photos; 1-year and 5-year retention).

  Verify: the docs match the specs, and no real data from `docs/sources/` appears.
- [x] 8.2 Review group 8 with `doc-updater` for consistency of the docs. Fix findings.

## 9. End-to-end and verification

- [ ] 9.1 Write `e2e/serial-state/privacy.spec.ts`:
  - an Admin filters the audit log by action and opens an entry;
  - an Admin registers a synthetic arquebusier, looks up their DNI, downloads the ZIP (the file name holds no DNI), erases them with a reference, and a new lookup finds nothing;
  - an Admin erases a synthetic FiringChief, who shows `ERASED` and cannot sign in;
  - a FiringChief gets "not allowed" on both pages;
  - axe passes on both pages.

  Verify: the spec passes in the compose stack (built from PowerShell) and the full suite passes twice in a row.
- [ ] 9.2 Run `verification-loop`:
  - build, types and lint;
  - backend and frontend tests, with coverage of at least 80 % on `AuditPrivacy`, the participants and `features/audit-privacy`;
  - a security grep: no DNI/NIE in URLs, logs, file names or audit data; no audit update outside `AuditMaintenance`; no FiringChief access;
  - a diff review.

  Run `e2e-runner` and `pr-test-analyzer` on the change. Verify: a PASS report, with the findings and follow-ups recorded in design.md.
