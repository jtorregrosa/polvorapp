# Spec Delta

## ADDED Requirements

### Requirement: Compliance warnings of edition entries (BR-04)
The system SHALL derive the compliance warnings of every `ACTIVE` `EditionEntry` of the edition in
progress from the arquebusier's current registry data, on the festival dates of the edition, and
SHALL never store them. The rules SHALL be those of "Compliance warnings (BR-04)", with these
reference dates (maintainer decision):
- the license warnings SHALL be evaluated on `festivalEndsOn`, so that the license must be valid
  through the last festival day. `LICENSE_EXPIRED` SHALL apply when the license's `expiresOn` is
  before `festivalEndsOn`;
- `UNDER_AGE` SHALL be evaluated on `festivalStartsOn`;
- `COURSE_MISSING`, `ID_PHOTO_MISSING` and `LICENSE_PHOTOS_MISSING` SHALL be evaluated on the
  current data;
- `LICENSE_EXPIRING` SHALL NOT apply to entries: a license that expires after the festival does
  not affect the order.

`RESERVE` entries, and entries of arquebusiers no longer in the registry, SHALL have no warnings. Entries of editions that are not
in progress SHALL show no warnings, because the registry data has changed since. Every warning
SHALL be a **warning**, never blocking: no entry edit, submission or validation SHALL be refused
because of it. The FiringChief attests on submission. The same arquebusier SHALL show the same
entry warnings on every screen of the order.

#### Scenario: License expiring during the festival
- **WHEN** the festival runs from 2031-04-22 to 2031-04-25 and an `ACTIVE` entry's license has `expiresOn` 2031-04-23
- **THEN** the entry has the warning `LICENSE_EXPIRED`

#### Scenario: License valid through the festival
- **WHEN** the festival ends on 2031-04-25 and an `ACTIVE` entry's license has `expiresOn` 2031-04-25
- **THEN** the entry has no license warning, even though it expires within 12 months

#### Scenario: Of age on the first festival day
- **WHEN** the festival starts on 2031-04-22, one `ACTIVE` arquebusier was born on 2013-04-22 and another on 2013-04-23
- **THEN** the first entry has no `UNDER_AGE` warning and the second has it

#### Scenario: Reserve entries are not checked
- **WHEN** a `RESERVE` entry's arquebusier has no license
- **THEN** the entry has no warning, while the registry still shows `LICENSE_MISSING`

#### Scenario: Warnings never block a submission
- **WHEN** a FiringChief submits an order whose `ACTIVE` entries have `LICENSE_EXPIRED` and `UNDER_AGE`, confirming the attestation
- **THEN** the order is submitted

### Requirement: First year (UC-07)
An arquebusier SHALL be in their **first year** in an edition when they have no `ACTIVE` entry in
any edition with an earlier `year`. The flag SHALL be derived and never stored. It SHALL be
**known** in an edition only when an edition with an earlier `year` has at least one comparsa
order (maintainer decision). Before that, PolvorApp has no history, and the flag SHALL NOT be shown
or counted.

The flag SHALL be shown:
- on each entry of an order, for the order's edition;
- on the arquebusier detail, for the current edition, when there is one.

An entry of an arquebusier no longer in the registry SHALL have no flag. Viewing the flag SHALL
record nothing.

#### Scenario: First year in the order
- **WHEN** the 2030 edition has orders, and an arquebusier with an `ACTIVE` 2031 entry had none in 2030 or before
- **THEN** their 2031 entry and their detail page show "First year"

#### Scenario: Reserve before does not count
- **WHEN** an arquebusier's only earlier entry is `RESERVE` in 2030
- **THEN** they are in their first year in 2031

#### Scenario: No history yet
- **WHEN** the current edition is the first edition with orders in PolvorApp
- **THEN** no entry and no arquebusier detail shows the first-year flag

#### Scenario: No current edition
- **WHEN** no edition is in progress
- **THEN** the arquebusier detail shows no first-year flag

## MODIFIED Requirements

### Requirement: Statistics (UC-07)
The API SHALL return statistics about the arquebusiers within the user's scope (BR-12). The
statistics SHALL optionally be filtered by one comparsa and by status (`ACTIVE` or `RESERVE`). They
SHALL include:
- the number of arquebusiers, and of `ACTIVE` and `RESERVE` ones;
- the number of arquebusiers of each `Gender` (`MALE`, `FEMALE`, `UNSPECIFIED`);
- the number of arquebusiers of each gender in each age bracket. The brackets are under 25, 25 to
  34, 35 to 44, and 45 or older. The age is derived from `birthDate` on today's date in
  Europe/Madrid;
- the number of arquebusiers of each gender with the training course done and not done;
- the number of arquebusiers in each license state: `VALID` not expiring, expiring (the warning
  `LICENSE_EXPIRING`), `EXPIRED`, `PENDING` and no license;
- the number of arquebusiers of each gender with and without at least one owned weapon, and the
  number of owned weapons of each `WeaponKind`;
- the number of arquebusiers of each gender in their first year in the current edition, and of
  those who are not. When there is no current edition, or the first-year flag is not known in it,
  the response SHALL say that these figures are unknown instead of returning zeros;
- when the user sees more than one comparsa and no comparsa filter is set: for each comparsa in
  scope that has arquebusiers matching the status filter, its number of arquebusiers, of `ACTIVE`
  and `RESERVE` ones, of each gender, and of those with at least one warning. The rows SHALL be
  sorted by comparsa name.

The response SHALL contain only counts and comparsa names. It SHALL contain no name, identifier,
national ID, birth date or other data of any arquebusier, because gender is kept only for equality
reports (`docs/compliance.md`). These rules SHALL be blocking:
- a comparsa outside the user's scope or that does not exist SHALL be answered `404 Not Found`;
- an unknown status SHALL be rejected with `400 Bad Request` naming `status`.

#### Scenario: FiringChief statistics
- **WHEN** a FiringChief assigned to one comparsa requests the statistics
- **THEN** every figure counts only the arquebusiers of that comparsa, and no per-comparsa rows are returned

#### Scenario: Admin statistics per comparsa
- **WHEN** an Admin requests the statistics without a comparsa filter
- **THEN** the response has a row for each comparsa with arquebusiers, sorted by name, whose totals add up to the overall total

#### Scenario: Filter by comparsa and status
- **WHEN** an Admin requests the statistics of "Comparsa Sintética Sur" with status `ACTIVE`
- **THEN** every figure counts only the active arquebusiers of that comparsa

#### Scenario: Comparsa outside the scope
- **WHEN** a FiringChief requests the statistics of a comparsa they are not assigned to
- **THEN** the API responds `404 Not Found` and returns no figure

#### Scenario: Unknown status is rejected
- **WHEN** the statistics are requested with status `INACTIVE`
- **THEN** the API responds `400 Bad Request` naming `status`

#### Scenario: Age bracket boundaries
- **WHEN** today is 2026-10-02 and one arquebusier was born on 2001-10-02 and another on 2001-10-03
- **THEN** the first is counted in the 25 to 34 bracket and the second in the under 25 bracket

#### Scenario: Owned weapons by kind
- **WHEN** the only arquebusier in scope owns a trabuco and a pistol
- **THEN** the statistics count one arquebusier with an owned weapon, one `TRABUCO` and one `PISTOL`

#### Scenario: First year by gender
- **WHEN** the previous edition has orders, and two women and one man in scope have no `ACTIVE` entry before the current edition
- **THEN** the statistics count two `FEMALE` and one `MALE` arquebusiers in their first year

#### Scenario: First year unknown
- **WHEN** the current edition is the first with orders
- **THEN** the statistics say that the first-year figures are unknown

#### Scenario: Only aggregates
- **WHEN** a user requests the statistics
- **THEN** the response contains counts and comparsa names only, and no name, identifier, national ID or birth date of any arquebusier

#### Scenario: Signed-out request
- **WHEN** a request without a session asks for the statistics
- **THEN** the API responds `401 Unauthorized`

### Requirement: Statistics screen
The UI SHALL offer a Statistics page in the main navigation to every signed-in user. It SHALL
follow the dashboard template:
- it SHALL offer a comparsa filter when the user sees more than one comparsa, and a status filter
  (all, active or reserve). The filters SHALL be kept in the page address;
- it SHALL show the figures of the statistics, each count with its share of the total as a whole
  percentage, formatted in the user's language. Shares SHALL be shown as text and as a bar, never
  by the bar alone;
- the gender figures across age brackets, course, owned weapons and first year SHALL form the
  equality report, shown as tables whose rows and columns are labelled for assistive technology.
  When the first-year figures are unknown, the report SHALL say that they will be available once
  an earlier edition has orders, instead of showing zeros;
- the per-comparsa rows SHALL be shown as a table, with each comparsa name linking to its detail
  page.

A FiringChief without assigned comparsas SHALL see the same empty state as on the start page. When
the filters match no arquebusier, the page SHALL say so with an action that clears the filters,
instead of showing percentages of zero. When the statistics cannot be loaded, the page SHALL say so
with a retry action. The page SHALL work on a phone (NFR-01), SHALL have no automatically
detectable WCAG 2.2 AA violations in either theme (NFR-07), and every text SHALL be available in
es-ES, ca-ES-valencia and en. It SHALL offer no download (exports belong to `add-exports`).

#### Scenario: Equality report
- **WHEN** an Admin opens the Statistics page
- **THEN** the page shows the number of women, men and unspecified arquebusiers, with their shares, overall and in each age bracket

#### Scenario: First year in the equality report
- **WHEN** an Admin opens the Statistics page while the first-year flag is known in the current edition
- **THEN** the equality report shows, for each gender, the arquebusiers in their first year with their shares

#### Scenario: First year not yet known
- **WHEN** a user opens the Statistics page while the current edition is the first with orders
- **THEN** the first-year table is replaced by a message saying the figures will be available once an earlier edition has orders

#### Scenario: Comparsa filter kept in the address
- **WHEN** an Admin filters the Statistics page by "Comparsa Sintética Norte" and reloads the page
- **THEN** the page still shows the statistics of "Comparsa Sintética Norte"

#### Scenario: Single comparsa
- **WHEN** a FiringChief assigned to one comparsa opens the Statistics page
- **THEN** no comparsa filter and no per-comparsa table are shown

#### Scenario: Filters with no arquebusier
- **WHEN** a user filters by a comparsa that has no reserve arquebusier and by status `RESERVE`
- **THEN** a translated "no arquebusier matches" message is shown with an action that clears the filters

#### Scenario: Shares are not shown by colour alone
- **WHEN** the gender figures are shown
- **THEN** each count is followed by its percentage as text, and the bar beside it only repeats that text visually and is hidden from assistive technology

#### Scenario: Statistics on a phone
- **WHEN** a user opens the Statistics page on a 360 px wide screen
- **THEN** every figure and table can be read without horizontal scrolling of the page
