# Design

## Context

See `proposal.md` for the motivation and `specs/notifications/spec.md` for the behaviour. The facts
below come from the current code and shape the approach.

- **Email.** `SharedKernel.Email` has `IEmailSender` (MailKit over SMTP, one connection per
  message, logs the template only) and `EmailMessage(ToAddress, ToName, Subject, TextBody,
  HtmlBody, Template)`, whose `ToString` prints the template only. `IEmailOutbox` is an
  **in-memory** channel: messages are lost on a restart, which suits password resets but not
  notifications. `PublicUrlOptions.Link(path, query)` builds UI links. It rejects an empty path
  and a `#` fragment. `IdentityEmails` renders `EmailTexts*.resx` (`<Template>.Subject` /
  `<Template>.Text`) in the user's `Locale`. It builds the HTML alternative from paragraphs with
  a private helper. The API image ships ICU for `ca-ES-valencia`.
- **No events between modules.** Modules talk through synchronous `.Contracts` only. A change and
  its audit entry commit together because every module context maps the audit table
  (`AuditTrailModel.AddAuditTrail`, which leaves the table out of the module's migrations) and
  calls `IAuditTrail.Record(context, record)` before `SaveChanges`.
- **Hooks.**
  - `OrderLifecycle.MoveAsync` (submit, validate, return) applies the move, records the audit
    entry, saves and commits once.
  - `EditionLifecycle.SetOrdersAsync` does the same for opening and closing the orders. It
    returns early, without writing, when nothing changes.
  - `CalendarMilestoneAdministration` adds, updates and removes milestones. A `CalendarMilestone`
    has `Date`, `Title` and `CreatedAt`.
- **Read contracts.**
  - `IEditionDirectory` returns `EditionSnapshot(Year, Status, OrdersOpen, festival dates, …)`,
    without `ordersCloseOn` or milestones.
  - `IEditionEntries.ListOrderStatusesAsync(editionId)` returns each comparsa's order status.
  - `IArquebusierFacts.ListAsync(comparsaIds)` returns `Status` and `ArquebusierLicenseFacts`
    (`Pending` | `Issued(…, ExpiresOn, …)`), without names.
  - `IUserDirectory` returns `UserSummary(Id, Name, Email, Role, Status)`, without `Locale` and
    without a way to list users by role.
  - `IFiringChiefAssignmentSource` answers user → comparsas only. Comparsa → FiringChiefs exists
    only as an endpoint.
- **Background work.** `StoredObjectSweeper` (host) is a `BackgroundService` with a
  `PeriodicTimer` on the injected `TimeProvider`. It is turned off in test hosts
  (`Storage:SweepEnabled=false`) and tested by calling its run method with a `FakeTimeProvider`.
  `FederationCalendar.Today(TimeProvider)` gives the date in Europe/Madrid. Host verbs implement
  `IHostCommand` (`Verb`, `RunAsync`), e.g. `create-admin`.
- **Frontend.**
  - `AccountPage.tsx` (identity-access) is built from `SectionGrid` and `SectionCard`.
  - `MilestonesSection.tsx` (festival-editions) uses the generated milestone hooks.
  - `components/app` has `CheckboxField` and `EditSheet`, but no switch.
  - i18n uses one namespace per feature in `src/i18n/locales/<locale>/`, typed from `es-ES`.

## Goals / Non-Goals

**Goals:**
- A notification is never lost once its cause is committed, and never recorded for a change that
  rolled back.
- Recipients, preferences and "does it still hold" are decided when sending, so an email is never
  sent to someone who should no longer get it.
- Every rule (who, when, what is due) is a pure function testable with a fake clock.
- Sending in the background never slows down or fails a request.

**Non-Goals:**
- A general domain-event bus. Only the five event types this change needs go through the outbox.
- Exactly-once delivery. SMTP gives no transaction, so a crash between handing a message over and
  recording it may send it twice (spec: Notification delivery).
- Several API instances at once. The design stays correct with them (locks, unique keys, leases),
  but it is not load-tested.

## Decisions

### D1. A `Notifications` module with a `notifications` schema

`Modules/Notifications` (`PolvorApp.Notifications`, `.Contracts`) owns everything about
notifications:
- the event outbox, the deliveries and the opt-outs;
- the rules for recipients and due dates;
- email rendering and the hosted services;
- the preference endpoints and the `send-notifications` host command.

It reads the contracts of `IdentityAccess`, `FederationCatalog`, `ArquebusierRegistry`,
`FestivalEditions` and `ComparsaOrders`. Its migration order is 70, after `Distribution`. It has
no cross-schema foreign keys:
- users are never deleted;
- editions, orders and milestones may go away, and a delivery about them is then skipped.

*Alternative:* add notification code to each module (orders emails from `ComparsaOrders`,
reminders from `FestivalEditions`). Rejected: recipients, preferences, retries and texts would be
duplicated across modules, and the capability would have no home (ADR-0001).

### D2. A transactional outbox, mapped like the audit trail

`Notifications.Contracts` holds three things:
- `NotificationEvent`, a closed set of records: `OrdersOpened(EditionId)`,
  `OrdersClosed(EditionId)`, `OrderSubmitted(EditionId, ComparsaId, OrderId)`,
  `OrderReturned(…)` and `OrderValidated(…)`;
- `INotificationOutbox.Record(DbContext context, NotificationEvent @event)`;
- `NotificationOutboxModel.AddNotificationOutbox(this ModelBuilder, bool ownsTable = false)`.

`FestivalEditionsDbContext` and `ComparsaOrdersDbContext` map the table, leaving it out of their
migrations. The notifications context owns it.

Hook sites:
- `EditionLifecycle.SetOrdersAsync` records an event only when the value really changes.
- `OrderLifecycle.Apply` records `OrderSubmitted` only for a FiringChief's submission, and
  `OrderReturned` / `OrderValidated` for every return and validation.

Each event is recorded before the existing `SaveAsync`, so it commits or rolls back with the
change and its audit entry.

```
notifications.notification_events
  id uuid PK (UUIDv7), type text NOT NULL CHECK (ORDERS_OPENED|ORDERS_CLOSED|ORDER_SUBMITTED|
  ORDER_RETURNED|ORDER_VALIDATED), edition_id uuid NOT NULL, comparsa_id uuid NULL, order_id uuid NULL,
  occurred_at timestamptz NOT NULL, processed_at timestamptz NULL
  ix_notification_events_pending (occurred_at) WHERE processed_at IS NULL
```

Events hold ids only, never names or reasons.

*Alternatives:*
- Publish after commit through an in-memory call. Rejected: a crash after commit loses the email,
  and a failure to publish would be silent.
- Add a contract the lifecycles call synchronously to send email. Rejected: requests would wait for
  SMTP, and a failure would either fail the change or be lost.

### D3. Deliveries: one row per recipient and topic

```
notifications.notification_deliveries
  id uuid PK (UUIDv7), user_id uuid NOT NULL, kind text NOT NULL CHECK (LICENSE_DIGEST|ORDER_WINDOW|
  ORDER_STATUS|MILESTONE_REMINDER), template text NOT NULL, topic text NOT NULL,
  data jsonb NOT NULL, status text NOT NULL CHECK (PENDING|SENT|SKIPPED|FAILED),
  attempts int NOT NULL DEFAULT 0, next_attempt_at timestamptz NOT NULL,
  created_at timestamptz NOT NULL, sent_at timestamptz NULL, last_error text NULL
  ux_notification_deliveries_user_topic UNIQUE (user_id, topic)
  ix_notification_deliveries_due (next_attempt_at) WHERE status = 'PENDING'
```

- **`topic`** is the deduplication key, and the unique index is the authority, used with
  `ON CONFLICT DO NOTHING`:
  - `event:<eventId>`
  - `digest:<yyyy-MM>`
  - `close7:<editionId>:<comparsaId>:<closeDate>` and `close1:…`
  - `milestone:<milestoneId>:<date>`

  A moved close or milestone date makes a new topic, so the reminder is due again (spec).
- **`data`** holds the ids, dates and counts needed to render the email and to check it still
  holds: the edition, the comparsa, the order and the status it announces, the milestone, or the
  digest counts per comparsa. It never holds addresses, names, titles or reasons. Comparsa names,
  the edition's year and milestone titles are read when the email is rendered.
- **`last_error`** holds the sender's phase and error code (e.g. `Send 550`), never server text.
- **Status values:**
  - `SKIPPED` is final. The recipient is no longer eligible, or the fact no longer holds (D6).
    It is not a failure.
  - `FAILED` is final after the retry window.
- **Retention:** the daily run deletes deliveries created more than 365 days ago, and processed
  events older than 30 days.

### D3b. Scheduled runs (added during implementation)

```
notifications.notification_runs
  job text, period text, ran_at timestamptz, PK (job, period)
```

The license digest is worked out once per month, on the first scheduled run of the month, and the run
records `(LICENSE_DIGEST, yyyy-MM)` in the same transaction, even when nothing was to be reported.
Without this record, a run later in the month could not tell "already done" from "missed", and a
license that started expiring mid-month would produce a digest that month (spec: "at most one digest
per month", "on the next day it runs in that month").

### D4. Opt-outs, not preferences rows

```
notifications.notification_opt_outs
  user_id uuid, kind text CHECK (…), created_at timestamptz, PK (user_id, kind)
```

A kind is on unless a row exists, so defaults need no data and new users need no seeding.
`NotificationKinds.For(UserRole)` lists the kinds that apply:
- FiringChief: all four kinds;
- Admin: `ORDER_STATUS` and `MILESTONE_REMINDER`.

A role change keeps the rows. Kinds that do not apply are ignored (spec).

### D5. Pipeline: events → deliveries → emails

Two hosted services in the module, plus one host command:

- **`NotificationDispatcher`** runs every `Notifications:DispatchIntervalSeconds` (default 30) on a
  `PeriodicTimer(TimeProvider)`. Each tick does two steps.
  1. **Expand events.** It locks up to 50 unprocessed events `FOR UPDATE SKIP LOCKED`. For each one
     it works out the candidate recipients (D6), inserts their deliveries and marks the event
     processed, in one transaction.
  2. **Send due deliveries.** It claims up to 50 `PENDING` rows whose `next_attempt_at` has
     passed. It leases them by moving `next_attempt_at` 10 minutes ahead and commits, so no
     transaction stays open during SMTP. Then for each row it:
     - re-checks eligibility and whether the fact still holds (D6), and marks it `SKIPPED` if not;
     - renders the email (D7);
     - calls `IEmailSender.SendAsync`;
     - marks it `SENT` with `sent_at`.

     On `EmailDeliveryException` it increments `attempts` and schedules the next attempt after
     1, 5, 15 and 60 minutes, then every 4 hours. Once 24 hours have passed since `created_at`, it
     marks the row `FAILED` and logs a Warning with the delivery id, template and error code.
- **`NotificationScheduler`** runs every 15 minutes. When the local time in Europe/Madrid is 08:00
  or later, it runs `ScheduledNotifications.RunAsync(today)`:
  - the license digest (D8), if any of this month's digests is missing;
  - the planned close reminders (D9);
  - the milestone reminders (D10);
  - the retention clean-up.

  Each step (digest, close reminders, milestone reminders, retention) runs in its own transaction
  under a transaction-scoped advisory try-lock (two-int key: class "PolN", step 1–4), and inserts
  deliveries with `ON CONFLICT DO NOTHING`. Running it again in the same day
  therefore adds nothing (spec: Scheduled notifications).
- **`send-notifications`** (`IHostCommand`) runs `ScheduledNotifications.RunAsync(today)` without
  the 08:00 check. It then runs the dispatcher's two steps until nothing is due or 2 minutes have
  passed, and exits 0, or 1 if a delivery failed in that run. It is used for operations, local
  checks and the E2E flow.

`Notifications:Enabled` (default `true`) turns both hosted services off. Test hosts set it to
`false`, as `Storage:SweepEnabled`, and call the run methods directly with a `FakeTimeProvider`.
Settings are validated at start-up and named, never echoed, as the email settings are.

*Alternative:* Quartz.NET or Hangfire. Rejected: one daily run and one polling loop do not justify
a new dependency, its tables and an ADR (NFR-09).

### D6. Recipients and "still holds", decided twice

`NotificationRecipients` reads three things:
- `IUserDirectory.ListAsync(UserRole role)` (new) → `UserSummary`, which gains `Locale` (one record
  instead of a near-duplicate `UserRecipient`; review of group 2), using the existing SQL projection.
  The renderer falls back to `es-ES` for a locale .NET does not know, so one bad row cannot fail a
  batch;
- `ICatalogDirectory.ListFiringChiefAssignmentsAsync()` (new) → `(UserId, ComparsaId)` for active
  comparsas, from `catalog.firing_chief_assignments`;
- the opt-outs.

| Delivery | Candidates |
|---|---|
| Orders opened / closed, close reminders | `ACTIVE` FiringChiefs with an assignment to an active comparsa (for reminders, to that comparsa) |
| Order returned / validated | `ACTIVE` FiringChiefs assigned to the order's comparsa |
| Order submitted | `ACTIVE` Admins |
| Milestone reminder | `ACTIVE` Admins, plus `ACTIVE` FiringChiefs when the edition is `IN_PROGRESS` |
| License digest | `ACTIVE` FiringChiefs with assignments, and something to count (D8) |

Candidates are filtered by their opt-outs when the deliveries are created, and the full check runs
again just before sending:
- **Recipient:** status `ACTIVE`, a role the kind applies to, not opted out, and still assigned
  to the comparsa when the email is about a comparsa.
- **Fact:**
  - opened/closed: `EditionSnapshot.OrdersOpen` still matches;
  - order emails: `IEditionEntries.ListOrderStatusesAsync` still shows the announced status;
  - close reminders: the orders are still open, the close date is unchanged, and the order is
    still not `SUBMITTED` or `VALIDATED`;
  - milestone: it exists, `notify` is still on and the date is unchanged;
  - digest: none, as it is a snapshot of that morning.

*Alternative:* decide only when the delivery is created. Rejected: during an SMTP outage, rows
can wait for hours, and the spec requires the check at send time.

### D7. Rendering

`NotificationEmails` renders each template from `NotificationTexts.resx` (Spanish, neutral),
`.ca` and `.en`, beside the module's `Emails/` folder, following the `IdentityEmails` pattern:
- `CurrentUICulture` is switched to the recipient's `Locale`;
- a missing key throws;
- dates are formatted with the recipient's culture as a long date (`d MMMM yyyy` style).

**Templates:**
- `OrdersOpened`
- `OrdersClosed`
- `OrdersClosingSoon` (7-day)
- `OrdersClosingTomorrow` (also used on the day itself)
- `OrderSubmitted`
- `OrderReturned`
- `OrderValidated`
- `MilestoneReminder`
- `LicenseDigest`

Each template has a `Footer.<Kind>` line ("You receive this because … — change it in your
settings") and the settings link. Order statuses are words from the same resx.

**Links** go through `PublicUrlOptions.Link`:
- `orders/{orderId}` and `orders`;
- `editions/{editionId}`;
- `/` (the alerts dashboard);
- `account` with `?section=notifications`. A query is used because `Link` refuses fragments.

The page scrolls to and focuses the section when that parameter is present.

**HTML alternative:** the private paragraph-to-HTML helper of `IdentityEmails` moves to
`SharedKernel.Email.PlainTextHtml`. It HTML-encodes every line, and comparsa names and milestone
titles are encoded too. Each link line becomes an anchor. The HTML has no images, styles from
remote URLs or tracking.

**Subject:** subjects never carry user-typed text except a milestone title. A title has no line
breaks (it is validated) and MimeKit encodes it.

**Example** (es-ES, `OrderReturned`):

```
Asunto: Pedido devuelto: Comparsa Sintética Sur — Fiestas 2031
Hola, Jefa Sintética:
La Federación ha devuelto el pedido de Comparsa Sintética Sur para las Fiestas 2031.
Lee el motivo y corrige el pedido en PolvorApp:
https://…/orders/…
—
Recibes este aviso porque tienes activados los avisos de estado de pedidos. Puedes cambiarlo en:
https://…/account?section=notifications
```

### D8. License digest rule

`LicenseDigestRule.Count(IEnumerable<ArquebusierFacts>, DateOnly date)` is a pure function. It
counts only `ACTIVE` arquebusiers:

| Count | Condition |
|---|---|
| `missing` | no license |
| `pending` | `Pending` |
| `expired` | `ExpiresOn < date` |
| `expiringSoon` | `date ≤ ExpiresOn < date + 90 days` |

These are the same comparisons as the compliance warnings, with a 90-day window instead of
12 months. The dashboard warning stays at 12 months. The digest is a narrower monthly nudge, and
the email's link leads to the full picture.

The scheduler reads `IArquebusierFacts.ListAsync` once for every assigned comparsa. Per
FiringChief, it builds `data` = the comparsas with a non-zero count (comparsa id and four
numbers), with topic `digest:<yyyy-MM>`. When the first run of the month happens on a later day
(the API was down), the counts are computed on that day (spec: missed first day).

### D9. Planned close reminders

These run when `EditionSnapshot` of the current edition has `OrdersOpen` and an `OrdersCloseOn`
(the snapshot gains `OrdersCloseOn`). With `days = OrdersCloseOn − today`:

| `days` | Reminder |
|---|---|
| 2–7 | `close7` |
| 0–1 | `close1` |
| below 0 or above 7 | none |

`IEditionEntries.ListOrdersAsync` (new, added during implementation: the reminders link to the order,
so they need its id) gives each prepared order with its comparsa and status; the comparsas whose
order is `SUBMITTED` or `VALIDATED` get no reminder. Every
other active comparsa (not prepared, `DRAFT`, `RETURNED`) gets one delivery per assigned
FiringChief. The `data` holds the status, so the email can say "your order is a draft / was
returned / is not prepared".

### D10. Milestones: `notify` and the reminder query

- **FestivalEditions migration:** `ALTER TABLE editions.calendar_milestones ADD notify boolean
  NOT NULL DEFAULT false`, so existing milestones stay off.
- **Request:** `CalendarMilestoneRequest` gains `notify` (`bool?`). Absent on add means `false`;
  absent on edit keeps the value, so older clients do not clear it. The response gains `notify`.
- **Audit:** the existing milestone audit records the field with its previous and new values.
- **Contract:** `IEditionDirectory.ListMilestonesToNotifyAsync(DateOnly from, DateOnly to)` (new)
  returns `MilestoneFacts(Id, EditionId, EditionYear, EditionStatus, Date, Title)` for editions
  that are not `CLOSED`, with `notify` on. The scheduler asks for `[today, today + 7]`, and topics
  use the milestone's date.

### D11. API

| Method and route | Who | Body / answer |
|---|---|---|
| `GET /api/account/notification-preferences` | any signed-in user | `{ kinds: [{ kind, enabled }] }`, the kinds of the user's role, in a fixed order |
| `PUT /api/account/notification-preferences` | any signed-in user | `{ kinds: [{ kind, enabled }] }`; answers as `GET` |

- **Validation** (`400`):
  - a kind that is not a code: `kinds[i].kind` with `invalid`;
  - a kind that does not apply to the role: `notApplicable`;
  - a duplicate kind: `duplicate`.

  Kinds left out of the body keep their value.
- **Writes and audit:** the save runs in one transaction. It inserts and deletes opt-outs, and
  records the audit entry `NotificationPreferencesChanged` (entity `User`, the user's id,
  `{ turnedOff: [...], turnedOn: [...] }`) only when something changed.
- **Rate limit:** none, like `PUT /account/locale`.
- **Milestone routes:** unchanged, apart from the `notify` field.
- **Contract file:** `contracts/openapi.json` is regenerated, with additive changes only.

### D12. Frontend

- **`features/notifications/`:**
  - `NotificationsSection` is a `SectionCard` in read mode. It shows each kind with a short
    description and "On"/"Off" as text.
  - Its edit `EditSheet` has one `CheckboxField` per kind.
  - It uses the generated hooks. It is rendered by `AccountPage` with the anchor id
    `notifications`, and it handles `?section=notifications`.
  - Its states are loading, error with retry, saved notice, and a failed save that keeps the
    values.
- **`MilestonesSection`:**
  - the add/edit form gains a `CheckboxField` "Send an email reminder", with a hint saying
    7 days before, to Admins, and to FiringChiefs once the edition is in progress;
  - each milestone with `notify` shows a text mark "Email reminder" (a `StatusBadge` with the
    neutral tone, plus an icon that is `aria-hidden`).
- **Confirm dialogs:** the "Open orders" and "Close orders" dialogs add a sentence that
  FiringChiefs are told by email.
- **i18n:** a new namespace `notifications` in `es-ES`, `ca-ES-valencia` and `en`, registered in
  `i18n/index.ts`, with these keys:
  - `section.title`, `section.edit`, `section.error`, `section.retry`;
  - `kinds.<KIND>.label` and `kinds.<KIND>.description` for the four kinds;
  - `state.on` and `state.off`;
  - `errors.invalid` and `errors.notApplicable`.

  New keys in `editions`:
  - `milestones.notify.label`, `milestones.notify.hint`, `milestones.notify.mark`;
  - `orders.confirmOpen.email` and `orders.confirmClose.email`.

### D13. Security and GDPR

- **SEC-01 / processors:** the SMTP provider receives users' names and addresses and the email
  contents. The Federation's DPA with it and SPF/DKIM on the Federation domain (NFR-11) are
  go-live checks.
- **Minimisation:**
  - emails carry counts, dates, comparsa names, statuses, milestone titles and links;
  - no arquebusier data or return reason;
  - links lead to pages that require sign-in and two-factor authentication.

  Deliveries and events store ids, dates and counts only.
- **BR-12:** the scope is re-checked when sending (D6). Event and digest data never crosses
  comparsas, because each FiringChief's digest is built from their own assignments only.
- **NFR-12:** logs carry the delivery id, template, kind and error code only.
  `EmailMessage.ToString` already hides the content.
- **SEC-05:** preference changes and milestone `notify` changes are audited. Sending is not an
  audited write. The delivery table is its record.
- **SEC-09 / UC-26 (#15):** deliveries and opt-outs are personal data of users, linked by user id.
  The GDPR export and erasure of #15 must include them. This is noted in `docs/compliance.md`.
- **Abuse:** an Admin toggling the orders repeatedly could make many emails. The "still holds"
  check drops the stale ones, and deliveries are per event, so the volume is bounded by the
  Admin's own actions, which are audited.

### D14. Tests

- **Unit tests:**
  - `LicenseDigestRule` (each count, `RESERVE` excluded, the 90-day edges);
  - due windows for `close7` and `close1`, and milestones (0–7 days, moved dates);
  - `NotificationKinds.For(role)`;
  - the retry schedule;
  - topic building;
  - rendering in three locales, with golden texts and no forbidden data.
- **Testcontainers tests** with Mailpit and `FakeTimeProvider`:
  - the outbox commits and rolls back with the order and edition moves;
  - dispatcher expansion and sending;
  - re-checks (deactivated, unassigned, opted out, fact changed);
  - retries with a failing fake sender, up to `FAILED`;
  - the lease after a simulated crash;
  - scheduler idempotency and the 08:00 rule;
  - the `send-notifications` command;
  - preference endpoints and their audit;
  - the milestone `notify` migration, API and audit;
  - `ModelDriftTests` with the new context;
  - architecture and module registration tests.
- **Vitest + axe:** `NotificationsSection` and `MilestonesSection`.
- **Playwright (Mailpit API):**
  - a FiringChief turns off `ORDER_WINDOW` and an Admin returns their order; the email arrives in
    Mailpit and no "orders closed" email comes after the Admin closes the orders;
  - an Admin marks a milestone to notify and runs `send-notifications` in the compose stack; the
    reminder arrives.

  The E2E compose sets `Notifications__DispatchIntervalSeconds=2`.

## Risks / Trade-offs

- **Duplicate on crash:** a crash between SMTP acceptance and marking the row `SENT` resends the
  email after the 10-minute lease. → Accepted and documented in the spec. The window is small.
- **Long SMTP outage:** deliveries older than 24 hours fail. A digest or reminder is then lost.
  → Logged at Warning with the template and error code. The next run does not resend it, because
  the topic exists. An operator can check the logs. An Admin-facing view of failures is left to
  #15.
- **Clock and DST:** the 08:00 rule uses Europe/Madrid local time from `FederationCalendar`'s
  zone, so a DST change shifts nothing. → Covered by tests on both DST transition days.
- **Several instances:** expansion locks events `FOR UPDATE SKIP LOCKED`, the scheduler uses an
  advisory lock, and sending uses leases. The unique `(user_id, topic)` index is the authority.
  → Correct, but not load-tested (non-goal).
- **Polling cost:** a query every 30 s on two small partial indexes. → Negligible at this scale
  (NFR, ~60 users).
- **Admin typos in milestone titles reach many inboxes.** → The reminder is sent 7 days ahead, so
  an Admin can still fix the title before then. The preview hint in the panel says when and to
  whom it is sent.
- **The digest's 90 days differ from the dashboard's 12 months.** → The email says "in the next
  90 days" explicitly, and links to the dashboard.

## Migration Plan

1. Deploy with the migrations (`migrate` command):
   - `notifications` schema (order 70);
   - `editions.calendar_milestones.notify` (default `false`).

   Existing milestones and users need no data.
2. The first scheduler run after deployment sends the current month's digest that morning,
   unless the deployment happens after the 1st and the operator prefers not to. This can be
   avoided by deploying on the 1st, or by setting `Notifications__Enabled=false` until the next
   month.
3. Rollback: turn the services off with `Notifications__Enabled=false`. The tables and the column
   are harmless if left in place. Reverting the code needs the `notify` column dropped only if the
   older model drifts. The `ModelDriftTests` of the older version would flag it.

## Research notes (task 1.1)

Versions in use: EF Core 10.0.12, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, MailKit/MimeKit
4.18.1, Microsoft.Extensions.TimeProvider.Testing 10.10.0, .NET SDK 10.0.100.

- **Shared table in several contexts**: EF documents mapping one entity in several bounded
  contexts with `ToTable(name, schema, t => t.ExcludeFromMigrations())`. That is exactly
  `AuditTrailModel.AddAuditTrail(ownsTable)`, so `AddNotificationOutbox(ownsTable)` copies it.
- **`ON CONFLICT DO NOTHING`**: EF has no upsert. Use `Database.ExecuteSqlAsync(FormattableString)`
  (parameterised) with an explicit `::jsonb` cast on the data parameter. `data` is mapped as a
  `string` with `HasColumnType("jsonb")`, as `AuditEntry.Data` is, so no dynamic JSON mapping is
  needed.
- **`FOR UPDATE SKIP LOCKED`**: `Database.SqlQuery<Guid>($"SELECT id AS \"Value\" … ORDER BY …
  LIMIT 50 FOR UPDATE SKIP LOCKED")` materialised directly (no LINQ composition, which would wrap
  it in a subquery), as `RegistryLocks` already does with `FOR UPDATE`.
- **Timers**: `new PeriodicTimer(period, timeProvider)` honours `FakeTimeProvider.Advance` (timer
  callbacks fire synchronously when time passes their due time). The repo already relies on it in
  `StoredObjectSweeperTests`. Rules are still tested through their run methods, not the timer.
- **Long dates**: the API image ships ICU (`InvariantGlobalization=false`). Each language's date
  pattern lives in the resx (`DateFormat`): `d 'de' MMMM 'de' yyyy` (es),
  `d MMMM 'de' yyyy` (ca: .NET uses the genitive month names "de febrer", "d’abril" after `d`)
  and `d MMMM yyyy` (en). The rendering tests assert the three outputs.
- **Subjects**: MimeKit encodes non-ASCII subjects (RFC 2047) when `MimeMessage.Subject` is set,
  so a milestone title with accents needs no handling. Line breaks are already refused by the
  milestone validation.

## Review notes (groups 2–5)

Reviews by `csharp-reviewer`, `database-reviewer`, `security-reviewer` and `silent-failure-hunter`
found no CRITICAL issue. Fixed:

- **One bad event stopped all email** (HIGH): the expander now takes one event per transaction; an event
  that cannot be planned (missing order or comparsa id, unknown type) is logged at Error and set aside;
  a failing expansion is logged and the pump sends the due deliveries anyway.
- **Deterministic failures were retried for a day; a failed save after a successful send caused a
  retry** (HIGH): a delivery that cannot be built or rendered fails at once; the "sent" record is written
  on its own and, if it fails, the row is left leased (logged at Error) and never retried from there —
  it may be sent again after the lease, the one duplicate the design accepts.
- **Leases**: recipients are read before claiming; batches are 10 rows (10 × 30 s SMTP timeout stays
  under the 10-minute lease); each row's lease is taken again with a compare-and-set before sending;
  every state change is a conditional update on a `PENDING` row; a failed batch gives its unreached
  rows back.
- **Re-checks at send time**: open/close emails need at least one assigned comparsa; order status emails
  check the recipient's current role against the template (submission → Admin, return/validation →
  assigned FiringChief) and that the order still belongs to the comparsa. The skip reason is stored in
  `last_error` (`RoleChanged`, `NotAssigned`, `OrderChanged`…).
- **Duplicates on quick toggles**: an order-window or order event superseded by a later event about the
  same edition window or order creates no delivery; only the latest is told.
- **Scheduled steps** run in separate transactions with a try-lock each, so one failing step (e.g. the
  registry read) never stops the reminders, and concurrent runs skip instead of timing out.
  `send-notifications` always sends what is due and exits 1 when a step, the expansion or a delivery
  failed.
- `EmailDeliveryException` carries the PII-free SMTP phase and code, stored as `last_error`.
- Rate limits: `PUT /account/notification-preferences` (`PersonalDataWrites`) and opening/closing the
  orders (`OrderWrites`), now that one request can email every FiringChief.
- Cancellation filters use the token (`!token.IsCancellationRequested`), so a timeout inside a contract
  never stops the host; the scheduler runs once at start-up; a disabled module logs a warning; an
  unknown locale is logged; a test checks the three resx files have the same keys.

Group 7 (`react-reviewer`, `a11y-architect`, no CRITICAL/HIGH): the notifications section derives
from the data (a failed refresh keeps the list and an open panel), an empty body is an error, an
email link focuses the section also when the load failed, loading is announced, a refused kind is
explained and the kinds reloaded; the milestone reminder is a column of its own ("Email reminder":
Yes/No) so the row header stays the title, with the text mark under the title on phones only.
`docs/design/patterns.md` documents both.

Not changed: deleting deliveries keeps a `created_at` index (cheap at this size); the
`OrderPreparationTests` flakiness seen in a full run (two owned weapons created in the same
millisecond compared in id order) predates this change.

## Verification (task 8.2)

**Result: PASS.**

- **Build and format:** `dotnet build` with 0 warnings; `dotnet format --verify-no-changes` clean;
  `tsc -b`, `eslint --max-warnings 0`, `prettier --check` and `check-i18n` clean.
- **Backend tests (Testcontainers):** 2188 of 2189 passed in the coverage run. The one failure is
  `OrderPreparationTests`, a test this change does not touch: it compares two owned weapons created
  in the same millisecond in id order (`LenderLookupTests`, with the same pattern, failed once in
  another run). The
  notifications namespace has 104 of 104 passing, including the review-driven tests: per-kind
  opt-out, role change end to end, close reminders for `VALIDATED`/`RETURNED`, closed or past
  milestones, the Madrid date at the year boundary, and a mixed invalid save. Architecture tests:
  26 of 26.
- **Backend line coverage:**
  - `PolvorApp.Notifications` 91.8 %;
  - `PolvorApp.Notifications.Contracts` 100 %;
  - backend total 96.0 %.
- **Frontend:** Vitest 2226 of 2226 passed, 95.4 % line coverage overall:
  - `NotificationsSection` 100 %;
  - `MilestonesSection` 85 %;
  - `SectionCard` 100 %.
- **E2E:** the Playwright suite passed twice in a row on the rebuilt, seeded compose stack (279
  passed each time, with `NOTIFICATIONS_DISPATCH_INTERVAL_SECONDS=2`).
  `serial-state/notifications.spec.ts` covers:
  - the preference opt-out and that it persists;
  - axe on the page and the panel;
  - the order return email without the reason;
  - the orders window opt-out;
  - the milestone reminder sent by `docker compose run --rm -T api send-notifications`.

  The E2E reads Mailpit by a baseline of message ids instead of clocks, and matches subjects in three
  languages, because other specs change the seeded users' language. Locally, Mailpit keeps 500
  messages: a stack with hundreds of E2E-created users fans a milestone reminder out to all of them.
- **Security grep:**
  - no anonymous route;
  - logs carry ids, templates, codes and exception types only;
  - events and deliveries hold no names, addresses, titles or reasons;
  - audit data holds kind codes only;
  - no binary file, workbook or `docs/sources` content was added.
- **Agents:** `pr-test-analyzer` found no critical gap; its important gaps were added as tests.
  `e2e-runner` found no blocker; its findings (vacuous assertion, clock comparison, timeouts,
  clean-up, axe on dialogs) were fixed.

## Open Questions

- The Federation's SMTP provider and sender domain (NFR-11) are a deployment concern. They do not
  change this design.
