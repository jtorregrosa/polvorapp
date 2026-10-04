# Proposal

## Why

Every change since `add-identity-access` writes an append-only audit entry, but nobody can read them
yet. When a FiringChief disputes a figure, or the Arms Authority asks who changed a list, the
Federation has no answer without a database console. The Federation is also the data controller
for about 800 arquebusiers, the external weapon owners and the ~60 users. GDPR gives each of them a
right of access and a right to erasure. Today PolvorApp can satisfy neither. Earlier changes kept
the history copies in edition entries and loans until "a GDPR erasure request (#15)" (BR-14,
SEC-08), and left audit retention and the database-level append-only guard "to be decided in #15".
Go-live needs all of this.

Capability (from `docs/mvp.md`): **`audit-privacy`**, change #15 of the sequence. It implements:
- **UC-25**: the audit log (who changed what), for Admins.
- **UC-26**: GDPR requests. An Admin exports or erases a person's data.

It relies on these rules and decisions:
- **SEC-05**: the audit log of every change and export. It becomes readable and gets a retention
  period. Its own reads, exports and erasures are audited too.
- **SEC-08 / BR-14**: an arquebusier's deletion keeps the edition history. The history is
  anonymised only by a GDPR erasure, which this change provides.
- **SEC-09**: data subject rights. The export and the erasure cover registry, history, loan,
  user, assignment and notification data. That includes the notification deliveries and opt-outs.
- **SEC-06 / SEC-02 / NFR-12**: an export holds only the person's own data, is built on request,
  is never stored, is sent with `no-store` and is audited before it is sent. DNI/NIE never travels
  in a URL or reaches the logs.
- **BR-12**: everything in this change is for Admins only.
- **ADR-0001** (the audit module reaches other modules only through contracts), **ADR-0002**,
  **ADR-0005** (photos in private storage), **ADR-0008** (ClosedXML), **ADR-0007**, **ADR-0009**,
  **ADR-0011**.

## What Changes

- **Audit log viewer (UC-25)**:
  - an Admin-only "Audit log" page lists entries newest first, paged on the server;
  - filters: period, user, comparsa, area (entity type), action and record;
  - each entry shows the time in Europe/Madrid, the user (or "System" / "Anonymous"), a translated
    action, the record with a link while it still exists, the comparsa, and a detail panel with the
    recorded data and the trace id;
  - the arquebusier and user detail pages give Admins a "View history" action that opens the log
    filtered by that record;
  - reading the log is not audited, because it writes and exports nothing.
- **Audit retention (maintainer decision)**:
  - access and security events (sign-ins, failed sign-ins, lockouts, recovery-code use, password
    reset requests, lender and personal-data lookups) are kept for **1 year**;
  - every other entry (changes, exports, GDPR requests) is kept for **5 years**;
  - a daily background purge enforces both periods and records what it removed, as counts.
- **Database append-only guard**: a PostgreSQL trigger refuses `UPDATE`, `DELETE` and `TRUNCATE` on
  the audit table. The only exception is a transaction that the audit module marks for its
  retention purge or a GDPR redaction. This closes the gap that `AppendOnlyAuditGuard` left open.
- **GDPR requests (UC-26)**, Admin only. Each needs a request reference (the Federation's own
  reference for the written request), which is audited instead of the person's identity:
  - **People by DNI/NIE** (maintainer decision: arquebusiers and external weapon owners):
    - a lookup shows what PolvorApp holds for that DNI/NIE: the registry record, the editions with
      entries, the loans as lender and the photos;
    - **export**: a ZIP with an Excel workbook (one sheet per category, in the Admin's language)
      and the person's photos as JPEG (maintainer decision). Other people's identities are left
      out of it;
    - **erasure** (maintainer decision: always allowed, with a warning). It deletes the registry
      record through the existing deletion (BR-14). It then anonymises every edition entry copy
      and every lender copy of that person, and removes the pickup proxies of their entries. The
      confirmation warns about the editions not yet closed whose lists will no longer name them.
  - **Users** (maintainer decision: Admins and FiringChiefs):
    - **export** of the profile, comparsa assignments, notification preferences and deliveries,
      and the user's own audit activity;
    - **erasure** anonymises the user, who gets the new derived status `ERASED`. It removes their
      credentials, two-factor data, assignments, opt-outs and deliveries, and redacts their name
      and email from audit data. Audit entries keep the user's id. An Admin cannot erase themselves
      (so an active Admin always remains), and an erased user cannot be reactivated.
- **Erased entries**:
  - an entry erased by a GDPR request shows "Erased person" and cannot be edited;
  - it cannot be a pickup holder or proxy;
  - lists of people (exports and distribution lists) leave it out;
  - totals and billing still count it, so the edition's figures do not change.
- **Less personal data in the audit**: `UserUpdated` records that the name changed but not its
  values (existing entries are cleaned by the migration). A failed sign-in's attempted email is
  kept only for the 1-year security retention, and is redacted when the matching user is erased.

## Non-goals

- Self-service requests by arquebusiers, external owners or users. Arquebusiers never use the app;
  every request reaches the Federation in writing and an Admin carries it out.
- Rectification, restriction and objection requests: rectification is the normal edit;
  restriction and objection are handled outside the app.
- Erasing or anonymising audit entries beyond the redaction of personal values. Audit entries keep
  ids (accountability, GDPR Art. 5(2)).
- Downloading the audit log, a FiringChief view of it, alerts on audit events, or auditing reads of
  the log.
- A low-privilege database role for the runtime. It stays a go-live hardening item in
  `docs/compliance.md`; this change adds the trigger.
- Tracking request deadlines (one month, Art. 12) or storing the written requests.
- Changing what an arquebusier's deletion does (BR-14). Erasure builds on it.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `audit-privacy`:
  - "Audit trail is append-only" gains the database guard and its two exceptions (retention purge,
    GDPR redaction);
  - new requirements: the audit log viewer and its screens, retention, GDPR lookup, export and
    erasure for people and users, request auditing and privacy screens.
- `identity-access`:
  - "Users and roles": the `ERASED` status;
  - "User management by Admins": no reactivation of an erased user, and the `ERASED` filter;
  - "Security events are audited": no name values in `UserUpdated`, and the retention and
    redaction of the attempted email.
- `comparsa-orders`:
  - "Entry history (BR-14)": anonymisation by erasure;
  - new requirement "Erased entries" (display, read-only, totals).
- `exports`: new requirement: per-person exports leave erased entries out, and totals count them.
- `distribution`: new requirement: an erased entry is never a holder or proxy, and is left out of
  the lists.

## Impact

- **Backend**:
  - `Modules/AuditPrivacy` gets:
    - the audit query endpoint with keyset paging;
    - the retention purge (a hosted service, plus a host command);
    - the GDPR lookup, export and erasure endpoints;
    - the ZIP and workbook builder;
    - the migration with the trigger;
    - a new `privacy` rate-limit policy.
  - `AuditPrivacy.Contracts` defines `IPersonalDataParticipant`. Registry, orders, distribution,
    identity, catalog and notifications implement it to describe, export and erase their part of a
    person's data in one transaction, and the GDPR flow calls each one.
  - Migrations:
    - `orders`: `erased_at` on edition entries and loans;
    - `identity`: `erased_at` on users;
    - `audit`: the trigger, and the clean-up of `UserUpdated` names.
  - The architecture test that forbids audit updates gets a single allowlisted maintenance class.
- **Frontend**:
  - a new `audit-privacy` feature: the Audit log page and the Privacy requests page;
  - "View history" on arquebusier and user details;
  - "Download personal data" and "Erase personal data" on user details;
  - "Erased person" in orders, and the `ERASED` user status;
  - a POST download helper;
  - the `audit` and `privacy` i18n namespaces in three locales, including a label for every audit
    action code;
  - the generated client.
- **Docs**:
  - `docs/compliance.md`: SEC-05 retention, SEC-08 and SEC-09 implemented, and the DB role as a
    go-live item;
  - `docs/data-model.md`: `erasedAt`, retention, BR-14;
  - `docs/glossary.md`: GDPR request, erasure, request reference;
  - `docs/use-cases.md`: notes for UC-25 and UC-26;
  - `docs/open-questions.md`: the four maintainer decisions;
  - `docs/development.md`: retention settings;
  - `docs/design/patterns.md`: the audit log and the GDPR request patterns;
  - the modules README: the personal-data participant convention.
- **Security and GDPR**:
  - erasure is irreversible and runs in one transaction across modules, and photos are erased
    after the commit with the hourly sweep as fallback;
  - DNI/NIE travels only in request bodies;
  - lookups are rate limited and audited without the DNI/NIE;
  - an export or erasure that cannot be audited does not happen;
  - the privacy notice (Q-50) must state the 1-year and 5-year audit retention.
