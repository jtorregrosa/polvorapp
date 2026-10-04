# Tasks

## 1. Research

- [x] 1.1 Check with Context7 (and `gh search code` for real-world usage) what designs D2–D7 rely on:
  - EF Core / Npgsql: a shared table mapped by several contexts with `ExcludeFromMigrations`; `INSERT … ON CONFLICT DO NOTHING` from EF (raw SQL or `ExecuteSqlInterpolated`); `FOR UPDATE SKIP LOCKED` with `SqlQuery`;
  - `PeriodicTimer` with `TimeProvider` and `FakeTimeProvider` (the version in `Directory.Packages.props`);
  - .NET ICU long-date formats for `es-ES`, `ca-ES-valencia` and `en`;
  - MimeKit encoding of non-ASCII subjects.

  Record the versions and any workaround in design.md. Verify: design.md has a "Research notes" section and no open question beyond the SMTP provider.

## 2. Contracts in other modules

- [x] 2.1 Write tests for `IUserDirectory.ListAsync(UserRole)`: users of that role with name, email, status and locale; deactivated and invited users included with their status. Then implement it and add `Locale` to the returned record (design D6). Verify: the new and existing identity tests pass.
- [x] 2.2 Write tests for `ICatalogDirectory.ListFiringChiefAssignmentsAsync()`: `(UserId, ComparsaId)` for active comparsas only. Then implement it. Verify: the tests pass.
- [x] 2.3 Write tests for the `FestivalEditions` changes (design D10):
  - the migration adds `notify` with default `false`, and existing milestones read `false`;
  - `notify` on add (absent means `false`), edit (absent keeps the value) and in the response;
  - `400` for a non-boolean `notify`;
  - the audit records the previous and new `notify`;
  - FiringChiefs read it;
  - `EditionSnapshot` carries `OrdersCloseOn`;
  - `ListMilestonesToNotifyAsync(from, to)` returns only `notify` milestones of non-closed editions in the range, with year and status.

  Then implement them and update `ModelDriftTests`. Verify: the tests and every existing editions test pass.
- [x] 2.4 Review group 2 in parallel with `csharp-reviewer` and `database-reviewer`. Fix CRITICAL/HIGH findings.

## 3. Notifications module, outbox and preferences

- [x] 3.1 Create `PolvorApp.Notifications` and `.Contracts` (design D1):
  - the module class;
  - the coded enum `NotificationKind` (`LICENSE_DIGEST`, `ORDER_WINDOW`, `ORDER_STATUS`, `MILESTONE_REMINDER`);
  - `NotificationsDbContext` with the `notifications` schema and migration order 70;
  - the problem codes;
  - the `Notifications` options (`Enabled`, `DispatchIntervalSeconds`) validated at start-up, with names only;
  - registration in `Program.cs`, `PolvorApp.slnx` and the `Dockerfile`.

  Verify: `dotnet build` passes, and the architecture, module registration and `CodedEnumsTests` tests pass.
- [x] 3.2 Write database tests for the migration (design D2–D4):
  - the three tables, their check constraints, the unique `(user_id, topic)` index and both partial indexes;
  - a second delivery with the same user and topic is ignored by the insert helper.

  Then add the entities, the migration and the context to `ModelDriftTests`. Verify: the tests pass.
- [x] 3.3 Write tests for `INotificationOutbox.Record` and `AddNotificationOutbox` (design D2):
  - an event recorded in another module's context commits with its change and is absent after a rollback;
  - events hold ids only.

  Then implement them in `Notifications.Contracts` and map the table in `FestivalEditionsDbContext` and `ComparsaOrdersDbContext` (excluded from their migrations). Verify: the tests pass and `ModelDriftTests` stays green.
- [x] 3.4 Write tests for the hooks:
  - opening and closing the orders records `ORDERS_OPENED` / `ORDERS_CLOSED` only when the value changes;
  - a FiringChief's submission records `ORDER_SUBMITTED`, an Admin's does not;
  - return and validation record their event;
  - a rejected move (`409`, `400`) records nothing.

  Then add the calls in `EditionLifecycle` and `OrderLifecycle`. Verify: the tests and every existing editions and orders test pass.
- [x] 3.5 Write unit tests for `NotificationKinds.For(role)`. Then write endpoint tests for `GET` and `PUT /api/account/notification-preferences` (design D11):
  - defaults all on, and only the role's kinds in a fixed order;
  - turning off and on again;
  - `400` with `invalid`, `notApplicable` and `duplicate` naming `kinds[i].kind`;
  - omitted kinds kept;
  - `401` when anonymous;
  - a role change keeps the opt-outs and hides the kinds that do not apply;
  - one `NotificationPreferencesChanged` audit entry with the kinds turned on and off, and none for an unchanged save or a rejection.

  Then implement them. Verify: the tests pass.
- [x] 3.6 Update the modules README with the notification outbox convention (record events in your own transaction; ids only). Verify: the README matches the code.
- [x] 3.7 Review group 3 in parallel with `csharp-reviewer`, `database-reviewer`, `security-reviewer` and `type-design-analyzer`. Fix CRITICAL/HIGH findings.

## 4. Rules and rendering

- [x] 4.1 Write unit tests for `LicenseDigestRule` (design D8):
  - each count;
  - `RESERVE` excluded;
  - the edges at the digest date and at +90 days;
  - a comparsa with nothing left out;
  - an empty result.

  Then implement it. Verify: the tests pass.
- [x] 4.2 Write unit tests for the due rules (design D9, D10):
  - `close7` for 2–7 days and `close1` for 0–1 days;
  - none outside these windows or with the orders closed;
  - `SUBMITTED` and `VALIDATED` excluded;
  - milestones 0–7 days ahead;
  - topics that change with a moved date;
  - the 08:00 Europe/Madrid gate, including both DST transition days.

  Then implement them. Verify: the tests pass.
- [x] 4.3 Write unit tests for the retry schedule: 1, 5, 15 and 60 minutes, then every 4 hours, and `FAILED` after 24 hours. Then implement it. Verify: the tests pass.
- [x] 4.4 Move the paragraph-to-HTML helper of `IdentityEmails` to `SharedKernel.Email.PlainTextHtml` with no behaviour change, and add tests for encoding and links. Verify: the identity email tests pass unchanged.
- [x] 4.5 Write rendering tests for every template (design D7), in `es-ES`, `ca-ES-valencia` and `en`:
  - subjects;
  - long dates in the recipient's language;
  - order status words;
  - comparsa names and milestone titles HTML-encoded;
  - the footer naming the kind, and the settings link `account?section=notifications`;
  - links under the public base URL;
  - no arquebusier name, DNI/NIE or return reason (synthetic sentinel values);
  - a missing text throws.

  Then add `NotificationTexts.resx`, `.ca.resx` and `.en.resx` and `NotificationEmails`. Verify: the tests pass.
- [x] 4.6 Review group 4 in parallel with `csharp-reviewer` and `security-reviewer` (email content, HTML injection). Fix CRITICAL/HIGH findings.

## 5. Dispatch, schedule and command

- [x] 5.1 Write Testcontainers tests for event expansion (design D5, D6):
  - each event type creates deliveries for the right candidates, with opt-outs filtered;
  - `INVITED` and `DEACTIVATED` users are excluded;
  - other comparsas' FiringChiefs are not included;
  - events are marked processed;
  - two expanders running at once do not duplicate deliveries.

  Then implement the expansion step. Verify: the tests pass.
- [x] 5.2 Write Testcontainers tests with Mailpit for sending:
  - an order return reaches the comparsa's FiringChiefs in their language;
  - re-checks mark rows `SKIPPED` for a deactivated, unassigned, opted-out or role-changed user, and for an event that no longer holds (orders toggled, order status changed, milestone moved or turned off, orders closed before a reminder);
  - a failing fake sender is retried and the email arrives once;
  - after 24 hours the row is `FAILED` and logged without address, subject or body;
  - a leased row abandoned by a crash is retried after the lease.

  Then implement the sending step and `NotificationDispatcher`. Verify: the tests pass.
- [x] 5.3 Write Testcontainers tests for `ScheduledNotifications.RunAsync(today)` with `FakeTimeProvider`:
  - the monthly digest (counts per comparsa, one email for two comparsas, none with nothing to report, sent on the 2nd after a missed 1st, once per month);
  - the close reminders and milestone reminders for the spec scenarios;
  - running twice sends each once;
  - the retention clean-up removes deliveries older than 365 days and processed events older than 30 days.

  Then implement it and `NotificationScheduler`. Verify: the tests pass.
- [x] 5.4 Write tests for the `send-notifications` host command: it ignores the 08:00 gate, sends what is due, exits 0, and exits 1 when a delivery failed in the run. Write tests that both hosted services are registered and stay off with `Notifications:Enabled=false` (set in `ApiFactory`). Then implement them. Verify: the tests pass.
- [x] 5.5 Write a test that the SMTP server being unreachable does not fail or slow down opening the orders or returning an order, and the emails arrive once it is back. Verify: the test passes.
- [x] 5.6 Review group 5 in parallel with `csharp-reviewer`, `database-reviewer`, `security-reviewer` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 6. API contract, seed and documentation

- [x] 6.1 Regenerate `contracts/openapi.json` and check `OpenApiDocumentTests`: the preference routes, the `notify` field and the problem codes. Verify: the tests pass and the diff is additive.
- [x] 6.2 Write tests for the seed (spec: Synthetic notification data):
  - a milestone of the current edition with `notify` on within 7 days of the seed date, and one with it off;
  - one kind turned off for one seeded FiringChief;
  - running twice creates them once;
  - no email is sent while seeding.

  Then extend `EditionSeeder` and add `NotificationSeeder`. Verify: the tests pass and `seed` runs twice on the compose stack.
- [x] 6.3 Update the docs:
  - `docs/data-model.md`: `CalendarMilestone.notify`, notification opt-outs and deliveries;
  - `docs/glossary.md`: `NotificationKind` and its four values, license digest, planned close reminder;
  - `docs/use-cases.md`: a UC-23 note with the maintainer decisions;
  - `docs/compliance.md`: email contents, the SMTP provider as processor, the delivery record and its one-year retention, preference audit, and #15 covering deliveries and opt-outs;
  - `docs/open-questions.md`: the maintainer decisions dated 2026-10-04 (FiringChiefs only for licenses, monthly digest at 90 days, per-user preferences, the four kinds);
  - `docs/development.md`: the `Notifications__*` settings, the `send-notifications` command and Mailpit.

  Verify: the docs match the spec, and no real data from `docs/sources/` appears.
- [x] 6.4 Review group 6 with `doc-updater` for consistency of the docs. Fix findings.

## 7. Screens

- [x] 7.1 Regenerate the API client and add the `notifications` namespace in `es-ES`, `ca-ES-valencia` and `en`, registered in `i18n/index.ts`, and the new `editions` keys (design D12). Verify: `npm run typecheck` and `npm run check-i18n` pass.
- [x] 7.2 Write tests (Vitest + Testing Library + axe) for `NotificationsSection`:
  - the role's kinds with their descriptions and "On"/"Off" as text;
  - the edit sheet with one checkbox per kind;
  - save with "Changes saved" announced;
  - a failed save keeping the values with the translated reason;
  - load failure with retry, while the rest of the account page works;
  - `?section=notifications` scrolls to and focuses the section.

  Then implement it and render it in `AccountPage`. Verify: the tests and axe pass in both themes.
- [x] 7.3 Write tests for `MilestonesSection`:
  - the "Send an email reminder" checkbox with its hint on add and edit;
  - the text mark on milestones with `notify`;
  - FiringChiefs see the mark read-only.

  Then write tests for the "Open orders" / "Close orders" dialogs mentioning the email. Then implement them. Verify: the tests and the existing editions tests pass, and axe passes.
- [x] 7.4 Update `docs/design/patterns.md` (the notifications section on the account page; the milestone reminder mark). Verify: the note matches the screens.
- [x] 7.5 Review group 7 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 8. End-to-end and verification

- [x] 8.1 Set `Notifications__DispatchIntervalSeconds=2` for the E2E compose stack. Write `e2e/serial-state/notifications.spec.ts`:
  - the seeded FiringChief turns off the order window emails in the account page;
  - an Admin returns that FiringChief's comparsa's order, and the email arrives in Mailpit (via its API) with the order link and without the reason;
  - the Admin closes and reopens the orders, and no order window email reaches that FiringChief;
  - the Admin marks a milestone to notify, runs `send-notifications` in the stack, and the reminder arrives;
  - the state is restored at the end;
  - axe passes on the account page.

  Verify: the spec passes in the compose stack, and the full suite passes twice in a row.
- [x] 8.2 Run `verification-loop`:
  - build, types and lint;
  - backend and frontend tests with coverage of at least 80 % on the `Notifications` module, the new contracts and `features/notifications`;
  - a security grep: no unauthenticated preference route, no address, subject or body in logs, no personal data in events, deliveries or audit data, no arquebusier address used;
  - a diff review.

  Run `e2e-runner` and `pr-test-analyzer` on the change. Verify: the PASS report, with the findings and follow-ups recorded in design.md. `docs/mvp.md` marks #14 done when the change is archived.
