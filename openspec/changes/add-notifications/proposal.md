# Proposal

## Why

PolvorApp already holds the dates and states that FiringChiefs and Admins must react to: licenses
that expire, the order window, the edition's milestones and the review of each order. Today a
FiringChief notices them only by opening the app, and the Federation chases comparsas by phone and
message before each deadline. The Federation asked for email reminders to the FiringChiefs about
expiries and deadlines, and that they can be managed (Q-21, answer 21). The email pipeline exists
since `add-identity-access` (NFR-11), so the remaining piece is deciding what to send, to whom and
when, without spamming or leaking personal data.

Capability (from `docs/mvp.md`): **`notifications`**, change #14 of the sequence. It implements:
- **UC-23**: email notifications for licenses expiring, upcoming windows and deadlines, and order
  status changes.

It relies on these rules and decisions:
- **BR-04**: the license digest reports the compliance warnings about licenses. It is a reminder
  and never blocks anything.
- **BR-10**: the order window is opened and closed by hand. Its planned dates (`ordersOpenOn`,
  `ordersCloseOn`) are only a published plan, so reminders are based on the planned close date
  but are sent only while the orders are actually open.
- **BR-12**: a FiringChief is only told about their own comparsas. Order emails go to the
  comparsa's FiringChiefs, and a milestone of a `DRAFT` edition reminds Admins only, because
  FiringChiefs cannot see draft editions.
- **SEC-05 / NFR-12 / SEC-01**: preference changes are audited. Emails carry counts, dates and
  links, never names, DNI/NIE or free text. Their contents never reach the logs.
- **ADR-0001** (a module per capability; background work runs in process as hosted services),
  **ADR-0007** (emails in the recipient's language), ADR-0002, ADR-0009, ADR-0011.

## What Changes

- **Recipients** (maintainer decision): only users of PolvorApp who can sign in (`ACTIVE` status)
  receive notifications. Invited and deactivated users do not. Arquebusiers never receive email:
  their address is only for their FiringChief to contact them.
- **License digest** (maintainer decision): on the first day of each month, each FiringChief gets
  one email per month. It counts, per comparsa in their scope, the `ACTIVE` arquebusiers whose
  license is missing, pending, expired, or expires within the next 90 days. It links to the
  alerts dashboard. When there is nothing to report, no email is sent.
- **Order window** (maintainer decision):
  - when an Admin opens the orders of the current edition (including a corrections window),
    every FiringChief is told; when an Admin closes them, every FiringChief is told too;
  - 7 days and 1 day before the planned `ordersCloseOn`, while the orders are open, the
    FiringChiefs of each comparsa whose order is not yet submitted or validated get a reminder.
- **Order status** (UC-23, maintainer decision):
  - when an Admin returns or validates an order, its comparsa's FiringChiefs are told. A return
    email asks them to read the reason in the app, without quoting it;
  - when a FiringChief submits an order, the Admins are told so they can review it.
- **Milestone reminders** (maintainer decision): an Admin can mark a `CalendarMilestone` to
  `notify`. Seven days before its date, Admins and the FiringChiefs who can see the edition get a
  reminder. Moving the date sends the reminder again for the new date.
- **Preferences** (maintainer decision, "poder gestionarlas"): on their account page, each user
  turns each kind of notification that applies to their role on or off. Every kind is on by
  default. Every email links to these settings.
- **Delivery**: notifications are recorded in the same transaction as the change that causes
  them, then sent in the background in each recipient's language, retried on SMTP failures, and
  sent at most once per recipient. Scheduled emails go out in the morning, Europe/Madrid time.
- **Once only**: each recipient gets each notification once; the month's license digest is worked
  out on the month's first run and recorded, so it never runs again that month (design D3b).
- **Rate limits**: saving preferences and opening or closing the orders, which now emails every
  FiringChief, are rate limited per user.
- **Audit**: preference changes are audited. A milestone's `notify` flag is audited like any
  other milestone field. Sending an email writes no business data and is not audited, but its
  delivery record (recipient, kind and outcome, no content) is kept for a year.

## Non-goals

- Emails to arquebusiers, to addresses outside PolvorApp, or other channels (SMS, push,
  WhatsApp).
- Admin-wide on/off switches per notification kind, custom lead times, or editable email texts.
- An in-app notification centre or inbox, and a screen listing sent emails. Delivery failures
  are logged; the audit log viewer is #15.
- Notifications about registry changes, imports, transfers, distribution slots or proxies.
- Opening or closing the orders automatically on the planned dates (BR-10 keeps them manual).
- Quoting arquebusiers' names or DNI/NIE, or return reasons, in an email, and attachments.
- Email open or click tracking.

## Capabilities

### New Capabilities
- `notifications`: notification kinds, recipients and their scope, preferences, the license
  digest, order window and order status emails, milestone reminders, scheduling, delivery and
  retries, email content rules, audit, screens and synthetic data.

### Modified Capabilities
- `festival-editions`: a calendar milestone gets a `notify` flag (requirement "Calendar
  milestones"), set in the milestone panel and shown in the milestone list (requirement "Editions
  screens").

## Impact

- **Backend**:
  - A new module, `Modules/Notifications` (`PolvorApp.Notifications` and `.Contracts`) with a
    `notifications` schema: the pending notifications, the deliveries, the user preferences, a
    dispatcher and a daily scheduler running as hosted services, the email texts in three
    languages, and the preference endpoints.
  - `Notifications.Contracts`: what other modules record when something happens (orders opened
    or closed, order submitted, returned or validated), written in their own transaction.
  - `FestivalEditions` and `ComparsaOrders` record those notifications in their open/close,
    submit, return and validate operations. `FestivalEditions` adds `notify` to milestones (a
    migration) and exposes milestones to notify through its read contract.
  - Read contracts used to find recipients and facts: users with role, locale and status
    (`IdentityAccess`), FiringChief assignments (`FederationCatalog`), licenses per comparsa
    (`ArquebusierRegistry`), and order status per comparsa (`ComparsaOrders`).
- **Frontend**: a "Notifications" section on the account page, the "Send a reminder" option in
  the milestone panel and its mark in the milestone list, i18n in three locales, the generated
  client.
- **Docs**: `docs/data-model.md` (`notify`, preferences, deliveries), `docs/glossary.md`,
  `docs/use-cases.md` (UC-23 note with the maintainer decisions), `docs/compliance.md` (email
  content and the delivery record), `docs/open-questions.md`, `docs/development.md` (scheduler
  settings, Mailpit), and the modules README.
- **Security and GDPR**: email leaves the system and stays in mailboxes, so its content is kept to
  counts, dates, titles typed by Admins and links. Recipients are checked against their role,
  status and comparsa scope when the email is sent, not only when it is recorded. The SMTP
  provider is a processor (`docs/compliance.md` §3).
