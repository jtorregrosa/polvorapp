# audit-privacy Specification

## Purpose
Keeps an append-only record of who changed what and when, and of every export and security event,
so the Federation can reconstruct and justify its data (SEC-05, UC-25), and later serves GDPR
requests (UC-26).

## Requirements

### Requirement: Audit trail of writes and security events
Every write to business data, every export and every security event SHALL be recorded as an audit
entry with: the time (UTC), the acting user (or none for anonymous events), the action code, the
target entity type and identifier, the comparsa concerned when there is one, the request's
correlation identifier and a structured description of the change. An audit entry SHALL be stored
in the same transaction as the change it describes, so a change is never saved without its entry
and an entry is never saved for a change that failed. Audit entries SHALL NOT contain passwords,
authenticator keys, one-time codes, recovery codes, invitation or reset tokens, or photo content.

#### Scenario: Entry stored with the change
- **WHEN** a user-management change is saved
- **THEN** exactly one audit entry describing it exists with the actor, action, target, time and correlation identifier

#### Scenario: Failed change leaves no entry
- **WHEN** a change fails and its transaction is rolled back
- **THEN** no audit entry for it is stored

#### Scenario: Secrets are never recorded
- **WHEN** a password change, invitation acceptance or recovery-code sign-in is audited
- **THEN** the audit entry contains no password, token or code value

### Requirement: Audit trail is append-only
The application SHALL provide no operation that updates or deletes audit entries. The database
SHALL itself refuse every `UPDATE`, `DELETE` and `TRUNCATE` of audit entries, except inside a
transaction that the audit capability explicitly marks for one of exactly two maintenance tasks:
- the retention purge (see "Audit retention"), which deletes expired entries;
- the redaction of a user's personal values in audit data during a GDPR erasure (see "Erasing a
  user's data (UC-26)"), which only replaces those values and never deletes an entry.

No other code path SHALL mark a transaction for audit maintenance.

#### Scenario: No modifying endpoint
- **WHEN** the API description is inspected
- **THEN** it contains no endpoint that updates or deletes audit entries

#### Scenario: Database refuses a direct update
- **WHEN** a statement updates or deletes an audit entry outside a maintenance transaction
- **THEN** the database rejects it and the entry is unchanged

#### Scenario: Maintenance transaction may purge
- **WHEN** the retention purge deletes expired entries inside its maintenance transaction
- **THEN** the database accepts the deletion

### Requirement: Audit log query (UC-25)
Admins SHALL be able to read the audit trail, newest first, in pages of at most 100 entries (50 by
default), with a cursor for the next page. Every filter SHALL be optional, and the filters SHALL be
combined:
- `from` and `to`, dates in Europe/Madrid, both included, with `from` not after `to`;
- the acting user, or only entries without one;
- the comparsa;
- the entity type and, with it, an entity id;
- the action code.

Each entry SHALL show:
- the time;
- the acting user's id and current name. An erased user's name SHALL be shown as erased, and an
  entry without a user SHALL be shown as system or anonymous;
- the action, the entity type and id, the comparsa id and name, the trace id, and the recorded data.

Invalid filters SHALL be blocking (`400 Bad Request` naming the field): an unknown action or
entity type code, a malformed date or id, `from` after `to`, a page size outside 1–100, or a
malformed cursor. A FiringChief SHALL receive `403 Forbidden`. Reading the audit log writes and
exports nothing and SHALL NOT be audited.

#### Scenario: Admin reads the latest entries
- **WHEN** an Admin requests the audit log without filters
- **THEN** the 50 newest entries are returned newest first, with a cursor for the next page

#### Scenario: Next page continues without gaps
- **WHEN** an Admin requests the page after a cursor while new entries are being written
- **THEN** the page continues from the last entry of the previous page with no entry repeated or skipped

#### Scenario: Filter by record
- **WHEN** an Admin filters by entity type `Arquebusier` and the id of a synthetic arquebusier
- **THEN** only the entries about that arquebusier are returned

#### Scenario: Filter by user and period
- **WHEN** an Admin filters by a FiringChief and the period 2030-03-01 to 2030-03-31
- **THEN** only that user's entries of March 2030 in Europe/Madrid are returned

#### Scenario: Invalid period is blocking
- **WHEN** an Admin filters with `from` 2030-04-01 and `to` 2030-03-01
- **THEN** the request is rejected with `400 Bad Request` naming `from`

#### Scenario: Unknown action is blocking
- **WHEN** an Admin filters by the action `Teleported`
- **THEN** the request is rejected with `400 Bad Request` naming `action`

#### Scenario: FiringChief is refused
- **WHEN** a FiringChief requests the audit log
- **THEN** the request is rejected with `403 Forbidden`

#### Scenario: Reading is not audited
- **WHEN** an Admin reads three pages of the audit log
- **THEN** no audit entry is written

### Requirement: Audit log screens
The UI SHALL give Admins an "Audit log" item in the main navigation, opening a list page with:
- filters for the period (the last 30 days by default), the user, the comparsa, the area (entity
  type) and the action, kept in the address;
- a table of entries with the time in the user's language and Europe/Madrid, the user, a
  translated action label, the record, and the comparsa;
- a "Show more" control that loads the next page.

Every action code SHALL have a label in the three locales; an action without one SHALL show its
code. The record SHALL link to its page while the record still exists and has a page:
arquebusier, user, comparsa, weapon model, edition or order. Opening an entry SHALL show its
details: the time, user, action, record, comparsa, trace id and each recorded data field with its
value.

The arquebusier and user detail pages SHALL give Admins a "View history" action that opens the
audit log filtered by that record. FiringChiefs SHALL see neither the navigation item nor the
action, and SHALL get the "not allowed" page at the address.

#### Scenario: Admin filters by action
- **WHEN** an Admin chooses the action "Order validated" on the audit log page
- **THEN** the table shows only validated-order entries, and the address holds the filter

#### Scenario: History of an arquebusier
- **WHEN** an Admin chooses "View history" on an arquebusier's page
- **THEN** the audit log opens filtered by that arquebusier, showing its registration and edits

#### Scenario: Deleted record has no link
- **WHEN** the audit log shows the deletion of an arquebusier
- **THEN** the record is shown without a link

#### Scenario: Entry details
- **WHEN** an Admin opens an `ArquebusierUpdated` entry
- **THEN** its details list the changed field names and the trace id

#### Scenario: FiringChief has no audit log
- **WHEN** a FiringChief opens the audit log address
- **THEN** the "not allowed" page is shown inside the shell and no audit data is requested

### Requirement: Audit retention
Audit entries SHALL be kept for a limited time (maintainer decision):
- **access and security events**, for 1 year: successful and failed sign-ins, lockouts,
  recovery-code use, password reset requests, refused Admin bootstraps, lender lookups and
  personal-data lookups;
- **every other entry**, for 5 years: changes, exports, downloads and GDPR exports and erasures.

A purge SHALL run once a day and delete the entries older than their period. It SHALL record one
audit entry with the number of entries it deleted per period, without an acting user, and SHALL
record nothing when it deletes nothing. Both periods SHALL be configurable, with these defaults.
They SHALL be refused at start-up when shorter than 1 year or 5 years respectively, or when not
whole numbers.

#### Scenario: Old sign-in removed
- **WHEN** the purge runs and a `SignedIn` entry is 366 days old
- **THEN** the entry is deleted, and one purge entry records one deleted security event

#### Scenario: Old change kept within five years
- **WHEN** the purge runs and an `ArquebusierUpdated` entry is 4 years old
- **THEN** the entry is kept

#### Scenario: Old change removed after five years
- **WHEN** the purge runs and an `ExportDownloaded` entry is 5 years and 1 day old
- **THEN** the entry is deleted

#### Scenario: Nothing to purge
- **WHEN** the purge runs and no entry has expired
- **THEN** no entry is deleted and no purge entry is recorded

#### Scenario: Too short a period is refused
- **WHEN** the application starts with the change retention set to 2 years
- **THEN** start-up fails naming the setting, without its value in the logs

### Requirement: GDPR request reference
Every GDPR export or erasure SHALL carry a request reference: 1 to 50 characters after trimming,
without line breaks, typed by the Admin to identify the written request the Federation received.
A reference that contains an email address or a valid DNI/NIE, however it is spaced, SHALL be
invalid. A missing or invalid reference SHALL be blocking (`400 Bad Request` naming `reference`).
The reference SHALL be recorded in the request's audit entry. The UI SHALL tell the Admin not to
type the person's name or DNI/NIE in it.

#### Scenario: Missing reference is blocking
- **WHEN** an Admin requests the erasure of a person without a reference
- **THEN** the request is rejected with `400 Bad Request` naming `reference`, and nothing changes

#### Scenario: A DNI in the reference is blocking
- **WHEN** an Admin types the reference "Solicitud 1234 5678 Z", where "12345678Z" is a valid DNI
- **THEN** the request is rejected with `400 Bad Request` naming `reference`, and nothing is audited

### Requirement: Looking up a person (UC-26)
Admins SHALL be able to look up everything PolvorApp holds about a person by an exact `nationalId`
(normalised and validated as a DNI or NIE, BR-01), sent in the request body and never in the
address. The answer SHALL say whether anything was found and summarise it:
- the registry record, when there is one: the name, comparsa, status and number of owned weapons
  and photos;
- for each edition with entries holding that `nationalId`, whether in the registry or in an entry's
  copy: the year, edition status, comparsa, order status, and whether the entry is already erased;
- the loans in which they are the lender, registered or external: the edition year and their number.

A `nationalId` that is not a valid DNI or NIE SHALL be blocking (`400 Bad Request` naming
`nationalId`). Lookups, exports and erasures SHALL be rate limited per user (`429 Too Many Requests`). Each lookup
SHALL be audited with the user, whether something was found and the request reference when given —
never the `nationalId` or a name; so SHALL an export or an erasure that finds nothing. A FiringChief
SHALL receive `403 Forbidden`.

#### Scenario: Registered arquebusier found
- **WHEN** an Admin looks up `00000000T`, the DNI of a synthetic arquebusier of "Comparsa Sintética Norte" with entries in 2029 and 2030
- **THEN** the answer shows the registry record and the 2029 and 2030 entries with their order status

#### Scenario: Former arquebusier found by the copy
- **WHEN** an Admin looks up the DNI of an arquebusier deleted from the registry whose 2029 entry keeps its copy
- **THEN** the answer shows no registry record and the 2029 entry

#### Scenario: External owner found
- **WHEN** an Admin looks up the DNI of an external owner who lent a weapon in 2030
- **THEN** the answer shows one loan as lender in 2030

#### Scenario: Nothing held
- **WHEN** an Admin looks up a valid DNI that appears nowhere
- **THEN** the answer says that PolvorApp holds no data for it, and the lookup is audited as not found

#### Scenario: Invalid DNI is blocking
- **WHEN** an Admin looks up `12345678A`, whose check letter is wrong
- **THEN** the request is rejected with `400 Bad Request` naming `nationalId`, and nothing is audited

#### Scenario: Lookup leaves no DNI in the audit
- **WHEN** an Admin looks up a DNI
- **THEN** the audit entry holds the user and whether something was found, and no DNI/NIE or name

#### Scenario: FiringChief is refused
- **WHEN** a FiringChief sends a person lookup
- **THEN** the request is rejected with `403 Forbidden`

### Requirement: Exporting a person's data (UC-26)
Admins SHALL be able to download, for a `nationalId` and a request reference, a ZIP file with:
- an Excel workbook in the Admin's language, with one sheet per category that holds data:
  - the registry record (identity, contact, birth date, gender, status, comparsa, license and
    course);
  - the owned weapons;
  - the edition entries (year, comparsa, status, powder, caps, weapon source, flask, rental model
    and the identity and weapon copy);
  - the loans in which they are the lender (year, weapon model, number, ownership guide and their
    identity as stored);
  - the pickup authorisations in which they take part (year, type, and whether they are the
    holder or the proxy);
  - an "About this data" sheet stating the controller, the purposes, the recipient categories and
    the retention, from translated texts;
- the person's stored photos as JPEG files named by their kind.

The file SHALL hold only the person's own data. Other people's names, DNI/NIE, contact and weapon
data SHALL be left out: the other party of a loan or a pickup authorisation appears only by its
role. Text cells SHALL be neutralised against formula injection. The file SHALL be built on
request, never stored, and sent with `Cache-Control: no-store`. The download SHALL be audited
before the file is sent, with the user, the request reference, the categories and their row
counts, and no `nationalId` or name. A file that cannot be audited SHALL NOT be sent
(`503 Service Unavailable`). A `nationalId` with no data SHALL be answered `404 Not Found`, and an
invalid one `400 Bad Request`. A FiringChief SHALL receive `403 Forbidden`.

#### Scenario: Export of an arquebusier
- **WHEN** an Admin exports the data of a synthetic arquebusier with an ID photo, two owned weapons and entries in 2029 and 2030
- **THEN** the ZIP holds a workbook with the registry, weapons, entries and "About this data" sheets, and the ID photo

#### Scenario: Borrower's identity left out
- **WHEN** an Admin exports the data of an external owner who lent a weapon to a synthetic arquebusier
- **THEN** the loans sheet shows the owner's own data and the weapon, and not the borrower's name or DNI/NIE

#### Scenario: Export is audited without identity
- **WHEN** an Admin exports a person's data with the reference "REQ-2030-07"
- **THEN** one audit entry records the user, "REQ-2030-07", the categories and their row counts, and no DNI/NIE or name

#### Scenario: Audit unavailable
- **WHEN** the audit entry of an export cannot be stored
- **THEN** the response is `503 Service Unavailable` and no file is sent

#### Scenario: Nothing to export
- **WHEN** an Admin exports a valid DNI that appears nowhere
- **THEN** the response is `404 Not Found`

### Requirement: Erasing a person's data (UC-26)
Admins SHALL be able to erase, for a `nationalId` and a request reference, everything PolvorApp
holds about that person, at any time (maintainer decision: never blocked, with a warning). In one
transaction, the erasure SHALL:
- delete their registry record as an arquebusier's deletion does (BR-14). Their owned weapons
  and photo records go with it, and so does their entry in the edition in progress while its
  orders are open;
- anonymise every remaining edition entry with that `nationalId`, in its copy or through the
  deleted arquebusier. It blanks the copied first name, last name, `nationalId`, `federationId`
  and owned weapon number and ownership guide, and marks the entry as erased with the time.
  Status, powder, caps, weapon source, flask, rental model and the order SHALL be kept;
- anonymise every loan in which they are the lender. It blanks the lender's names and
  `nationalId` and the weapon number and ownership guide, keeps the weapon model, and marks the
  loan as erased;
- remove every pickup proxy in which an erased entry is the holder or the proxy.

After the commit, their stored photo images SHALL be erased, with the stored-file sweep as
fallback. A second erasure of the same `nationalId` SHALL be answered `404 Not Found`. Before
confirming, the UI SHALL warn:
- that the erasure cannot be undone;
- about each edition that is not `CLOSED` where the person has an entry, naming its year, comparsa
  and order status, and saying that its lists will no longer name them;
- about the entry removed from the edition in progress while its orders are open.

The erasure SHALL be audited in its transaction with the user, the request reference and the counts
of what was deleted, anonymised and removed, and never the `nationalId` or a name. An erasure that
cannot be audited SHALL NOT happen. An invalid `nationalId` SHALL be blocking (`400 Bad Request`).
A FiringChief SHALL receive `403 Forbidden`.

#### Scenario: Erase a former arquebusier
- **WHEN** an Admin erases the DNI of an arquebusier deleted from the registry whose validated 2029 entry keeps its copy
- **THEN** the 2029 entry keeps its powder and status, its name, DNI/NIE and `federationId` are blank, and it is marked as erased

#### Scenario: Erase a registered arquebusier
- **WHEN** an Admin erases the DNI of a registered arquebusier with photos, while the orders of the edition in progress are closed and they have an entry in it
- **THEN** the registry record, weapons and photos no longer exist, the entry in the edition in progress is kept anonymised, and the stored images are erased

#### Scenario: Warning about an open edition
- **WHEN** an Admin opens the erasure confirmation of a person with an entry in the `VALIDATED` 2031 order of "Comparsa Sintética Norte", in an edition in progress
- **THEN** the dialog warns that the 2031 lists of "Comparsa Sintética Norte" will no longer name them, before the Admin confirms

#### Scenario: Lender anonymised
- **WHEN** an Admin erases the DNI of an external owner who lent a weapon in 2030
- **THEN** the borrower's loan keeps the weapon model, the owner's names, DNI/NIE, weapon number and guide are blank, and the loan is marked as erased

#### Scenario: Proxies removed
- **WHEN** an Admin erases a person whose entry is the powder proxy of another holder
- **THEN** that pickup proxy no longer exists, and the holder's entry is unchanged

#### Scenario: Totals unchanged
- **WHEN** an Admin erases a person with a 2 kg entry in a validated order
- **THEN** the order's totals and billing are the same as before the erasure

#### Scenario: Erasure is atomic
- **WHEN** an erasure fails while anonymising the loans
- **THEN** nothing is deleted or anonymised, and no erasure is audited

#### Scenario: Already erased
- **WHEN** an Admin erases a DNI that was erased before and appears nowhere else
- **THEN** the response is `404 Not Found`

#### Scenario: FiringChief is refused
- **WHEN** a FiringChief requests an erasure
- **THEN** the request is rejected with `403 Forbidden`, and nothing changes

### Requirement: Exporting a user's data (UC-26)
Admins SHALL be able to download, for a user and a request reference, a ZIP file with an Excel
workbook in the Admin's language holding:
- the profile: name, email, role, language, status, creation, last sign-in and whether two-factor
  authentication is enabled. No password, authenticator key, recovery code or token;
- the comparsa assignments;
- the notification preferences per kind;
- the notification deliveries: kind, template, status, attempts and dates;
- their activity: the audit entries in which they are the acting user or the target user, with the
  time, action, entity type and id, without the recorded data of entries about other people;
- an "About this data" sheet.

Delivery, protection, auditing and the `503` rule SHALL be as for a person's export. An unknown
user SHALL be answered `404 Not Found`. A FiringChief SHALL receive `403 Forbidden`.

#### Scenario: Export of a FiringChief
- **WHEN** an Admin exports the data of a FiringChief assigned to "Comparsa Sintética Norte" who turned `LICENSE_DIGEST` off and received two emails
- **THEN** the workbook holds the profile, the assignment, the preferences with `LICENSE_DIGEST` off, the two deliveries and the user's activity, and no secret

#### Scenario: FiringChief is refused
- **WHEN** a FiringChief requests a user's data export
- **THEN** the request is rejected with `403 Forbidden`

### Requirement: Erasing a user's data (UC-26)
Admins SHALL be able to erase a user's data with a request reference. In one transaction, the
erasure SHALL:
- replace the user's name with a fixed erased marker and their email with a unique address in the
  reserved `.invalid` domain;
- deactivate the user and mark them as erased with the time;
- remove their password, authenticator, recovery codes, tokens and remembered devices, and end
  their sessions;
- remove their comparsa assignments, notification opt-outs and notification deliveries;
- redact the user's former name and email wherever an audit entry's data holds them, keeping the
  entries.

Audit entries and order records SHALL keep the user's id. The following SHALL be blocking
(`409 Conflict` with a translated reason): erasing yourself (so an active Admin always remains),
and erasing a user who is already erased. An unknown user SHALL be answered `404 Not Found`. The erasure SHALL be audited in its transaction with the
user, the request reference and the counts of what was removed and redacted. An erasure that
cannot be audited SHALL NOT happen. A FiringChief SHALL receive `403 Forbidden`.

#### Scenario: Erase a FiringChief
- **WHEN** an Admin erases a deactivated FiringChief and confirms
- **THEN** the user shows as erased, cannot sign in, has no assignment, preference or delivery, and the audit log still shows their earlier actions under their id as an erased user

#### Scenario: Failed sign-ins redacted
- **WHEN** an Admin erases a user whose email appears as the attempted email of failed sign-ins
- **THEN** those entries are kept and no longer hold the email

#### Scenario: Self-erasure is blocking
- **WHEN** an Admin tries to erase their own account
- **THEN** the request is rejected with `409 Conflict`, and nothing changes

#### Scenario: Already erased
- **WHEN** an Admin tries to erase a user who is already erased
- **THEN** the request is rejected with `409 Conflict`, and nothing is audited

### Requirement: GDPR request screens
The UI SHALL give Admins a "Privacy requests" item in the main navigation, opening a page that:
- looks up a person by DNI/NIE and shows the summary of what PolvorApp holds, or that nothing is
  held;
- offers "Download data" and "Erase data" for a found person. Both ask for the request reference,
  with help that says not to type the person's name or DNI/NIE;
- confirms an erasure in a dialog that names the person, lists the warnings and repeats the verb.
  After it succeeds, the outcome is shown and the lookup is cleared.

The user detail page SHALL offer Admins "Download personal data" and "Erase personal data" under
"More actions", with the same reference field and confirmation. An erased user SHALL show the
`ERASED` status and no actions other than "View history". The DNI/NIE SHALL never be placed in the
address. FiringChiefs SHALL see none of this and SHALL get the "not allowed" page at the address.

#### Scenario: Admin finds and downloads
- **WHEN** an Admin looks up a synthetic DNI, chooses "Download data" and types the reference "REQ-2030-07"
- **THEN** the ZIP file is downloaded and the DNI does not appear in the address

#### Scenario: Erasure confirmed
- **WHEN** an Admin chooses "Erase data" for a found person, types a reference and confirms
- **THEN** the outcome is announced, and a new lookup of the same DNI says that nothing is held

#### Scenario: Erasure cancelled
- **WHEN** an Admin opens the erasure confirmation and cancels
- **THEN** nothing changes and nothing is audited

#### Scenario: FiringChief has no privacy page
- **WHEN** a FiringChief opens the privacy requests address
- **THEN** the "not allowed" page is shown inside the shell
