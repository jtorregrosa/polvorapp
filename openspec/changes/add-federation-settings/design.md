# Design

## Context

See `proposal.md` and the delta specs. The facts below come from the current code.

- **Settings row.** `FederationCatalog.Logos.FederationSettings` is a singleton row (`Id = 1`, check
  constraint) holding only the logo and `UpdatedAt`. Logo endpoints are in `FederationLogoEndpoints`
  (`GET /federation`, `GET`/`PUT`/`DELETE /federation-logo`).
- **Names.** `Exports.Contracts.FederationNames.Spanish` and `.Valencian` are constants, used by
  `Badges` and `Distribution` texts.
- **Email.**
  - The API host's `EmailOptions` (`Email:From`, validated at start-up) and the MailKit sender
    build every message: notifications, invitations and password resets.
  - Notification bodies end with the settings link (`Notifications` templates).
- **Reminders.** `Notifications.Rules.Schedule` has `MilestoneLeadDays = 7` and close reminders
  from 2 to 7 days, as pure functions of the date.
- **Screens.** The Federation logo section is at the bottom of `ComparsasPage`. Read-mode detail
  pages with `SectionCard` + `EditSheet` are the standard edit pattern (design-system
  "Detail pages in read mode").

## Goals / Non-Goals

**Goals:**
- One owner (`FederationCatalog`) and one read contract for global settings.
- Today's behaviour is unchanged until an Admin edits a value.
- A section-per-group API and UI that new groups can join without touching the others.

**Non-Goals:** a generic key–value settings store; settings versioning or history beyond the audit
log.

## Decisions

### D1. Typed columns on the existing row

`FederationSettings` gains typed columns:
- `official_name_es`, `official_name_ca`, `short_name`;
- `contact_email`, `website` and `reply_to`, nullable;
- `sender_name`, required (it starts as "PolvorApp");
- `close_reminder_lead_days` and `milestone_lead_days`, with check constraints 2–14 and 1–14;
- as defence in depth (group 2 review), checks that the names are not blank, that `sender_name`
  holds no control character, `<`, `>`, `"` or `@`, and that `website` starts with `https://`.

The row also gets an `xmin` version. The migration fills today's values (the names from
`FederationNames`, "Unión de Comparsas", "PolvorApp", 7, 7).

*Alternative*: a key–value table. Rejected, because it loses types, constraints and EF validation,
and every reader would parse strings.

### D2. API per section

- `GET /federation-settings` (`GetFederationSettings`) is Admin only and returns all sections with
  `version`. The version is the row's `xmin`, so a logo change or another section's save also
  changes it: the screen reloads the settings after every save and every logo change.
- `PUT /federation-settings/identity`, `/emails`, `/orders` and `/calendar` are Admin only. Each
  takes its fields plus `version` and returns `409` on a stale version. Each PUT records one audit
  entry (`FederationSettingsChanged`, the section, previous and new values).
- `GET /federation` (renamed `GetFederation`, `FederationResponse`) already exists for every
  signed-in user. It gains the identity fields
  (`officialNameEs`, `officialNameCa`, `shortName`) next to the logo flag, so the UI shell can use
  the short name later.
- Validation uses `InputFields`:
  - lengths are measured after trimming;
  - the sender name refuses `< > " @` and control or invisible characters (CR/LF, U+2028, bidi
    marks), against header injection and a name posing as an address;
  - email addresses (at most 254 characters) use `InputFields.IsPlainEmail`, as identity does: plain
    ASCII, no display name, a dotted domain;
  - the website (at most 200 characters) must be an absolute `https` address with a dotted ASCII
    host name (no IP, no `localhost`, no lookalike Unicode host), no credentials, written in its
    canonical form, so what is stored is exactly what emails show;
  - saves take a body of at most 8 KB and share the Admin write rate limit (`order-writes`).

### D3. `IFederationSettings` contract

`FederationCatalog.Contracts.IFederationSettings.GetAsync()` returns a `FederationSettingsSnapshot`
record, read with `AsNoTracking`. It is registered scoped and memoised for the request. Readers:
- `Badges` and `Distribution` texts take the official name for the document language;
- `Notifications` `Schedule` takes the lead days as parameters, so the functions stay pure, and the
  footer takes the short name and contact;
- the host's email sender takes the sender name and reply-to. The host already references the
  module contracts.

`FederationNames` is deleted once no reader is left. Its only readers are `BadgeTexts` and
`DistributionTexts`, which build the texts that `Exports` renders, so `Exports` itself needs no
change: the documents keep receiving the name from their callers.

### D4. Email header handling

`EmailOptions.From` keeps the address (only the address counts; a display name in it is ignored).
The host reads the name and reply-to through `IEmailSenderProfile` (SharedKernel), implemented with
`IFederationSettings` in a scope of its own for every message, which also gives the settings page
the sender address. The sender builds
`new MailboxAddress(settings.SenderName, options.From.Address)`. `MimeKit` encodes the display name.
D2's validation keeps line breaks out anyway. `ReplyTo` is added when set. When the settings cannot
be read (database down), sending already fails and is retried by the delivery rules. No fallback
name is invented. The failure is recorded as the `Compose` phase with the exception type as its
code. Invitations and password resets go through the in-memory outbox, which never retried an SMTP
failure either: a settings outage loses such an email, and the person asks again (group 3 review).

### D5. Screen

`SettingsPage` at `/settings` (Admin route) is a `RecordHeader`-less detail page with
`SectionGrid`:
- Identity, Federation logo (the moved `FederationLogoSection`), Emails, Orders, Calendar;
- each `SectionCard` shows `KeyFacts`, a "where it is used" sentence, and an `EditSheet` with
  `use-app-form` + Zod mirroring D2.

A navigation entry `{ to: '/settings', section: 'administration', icon: Settings, roles: ['ADMIN']
}` is added. The comparsas page drops the logo section.

### D6. i18n keys (es-ES, ca-ES-valencia, en)

- `catalog:settings.*`: title, description, section titles, "used in" sentences, field labels and
  hints, validation reasons, the save notice, "the contact must be the Federation's";
- `ui:nav.settings`;
- `notifications` footer templates: short name and contact lines.

## Research findings (task 1.1)

Context7, `gh` and the vendors' sites are not reachable from the implementation environment, so
the findings come from the pinned packages (MailKit/MimeKit 4.18.1, EF Core 10.0.12, Npgsql EF
Core 10.0.3), probed with a throwaway script, and from patterns already proven in this codebase.

- **MimeKit display name.** `new MailboxAddress(name, address)` writes a non-ASCII name as an
  RFC 2047 encoded word (`=?utf-8?b?…?=`). A name with CR/LF is also encoded
  (`=?utf-8?q?Evil=0D=0ABcc=3A?=`), so MimeKit itself does not allow header injection. D2's
  refusal of `< > "` and control characters stays as defence in depth and to keep names readable.
- **Reply-To.** `MimeMessage.ReplyTo` is an `InternetAddressList`; adding a `MailboxAddress` writes
  a `Reply-To:` header. Nothing is written when the list is empty.
- **Address parsing.** `MailboxAddress.TryParse` accepts `nodomain` (empty `Domain`) and
  `Name <a@b.c>` (with a display name). The settings validation therefore uses the stricter
  `InputFields.IsPlainEmail` that identity already applies to sign-in addresses.
- **`xmin` concurrency.** `ComparsaOrders` already maps `uint Version` with `IsRowVersion()`, which
  Npgsql maps to the `xmin` system column with no migration column. The settings row follows it,
  and its writes lock the row first (`SELECT … FOR UPDATE`), as editions do.
- **Check constraints** are declared with `ToTable(t => t.HasCheckConstraint(...))`, as the other
  catalogue constraints, and the migration emits them.
- **Per-section forms.** The detail pages here already save each `SectionCard` from its own
  `EditSheet` with `use-app-form` and a version (editions), which is the pattern D5 follows; no
  external example is needed.

No new open question.

## Risks / Trade-offs

- [A wrong reply-to or sender name affects every email] → Admin only, audited, and the panel shows
  a preview line ("From: {{name}} <{{address}}>").
- [A sender name can look like phishing (e.g. "Bank")] → Admin-only and audited. The address stays
  the deployment's verified domain.
- [Lead-time changes resend reminders] → Reminders are recorded per close date or milestone date
  and kind, so a longer lead time can trigger one earlier reminder only if not yet sent. This is
  documented in the Orders section's sentence.
- [Settings read on every email] → Memoised per request and a single-row query. The notification
  runner reads them once per run.

## Migration Plan

One EF migration adds the columns with defaults from today's values. Deploy API and web together.
Rollback: revert the change. The `Down` migration drops the columns, and documents return to the
constants.
