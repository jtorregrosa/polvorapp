# compliance-insights Specification

## Purpose
Derives compliance warnings from the arquebusier registry (BR-04) and turns them into insights: an
alerts dashboard that tells FiringChiefs and the Federation what needs attention (UC-06), and
statistics with an equality report (UC-07), always within the user's comparsa scope (BR-12).

## Requirements

### Requirement: Compliance warnings (BR-04)
The system SHALL derive the compliance warnings of every arquebusier from the registry and SHALL
never store them. The reference date SHALL be today in the Europe/Madrid time zone. The warnings
SHALL be:
- `LICENSE_MISSING`: the arquebusier has no license;
- `LICENSE_PENDING`: the license is pending;
- `LICENSE_EXPIRED`: the license status is `EXPIRED`;
- `LICENSE_EXPIRING`: the license status is `VALID` and `expiresOn` is earlier than the same day 12
  months after today;
- `COURSE_MISSING`: the arquebusier has no `trainingCompletedOn`;
- `UNDER_AGE`: the arquebusier's age, derived from `birthDate`, is under 18 years;
- `ID_PHOTO_MISSING`: the arquebusier has no `idPhoto`;
- `LICENSE_PHOTOS_MISSING`: the license is issued (not pending) and lacks its `frontPhoto`, its
  `backPhoto` or both.

At most one of the four license warnings SHALL apply at a time. The warnings SHALL apply to
arquebusiers with status `ACTIVE` and `RESERVE` alike (maintainer decision). Every warning SHALL be
a **warning**, never blocking: no registration, edit, status change, transfer, deletion or photo
operation SHALL be refused or delayed because of it. Every screen and response that shows
warnings SHALL use these rules and SHALL show the same warnings for the same arquebusier on the same
day. The warnings SHALL be returned in the order listed above.

#### Scenario: Expired license
- **WHEN** today is 2026-10-02 and an arquebusier's license has `expiresOn` 2026-10-01
- **THEN** the arquebusier has the warning `LICENSE_EXPIRED` and no other license warning

#### Scenario: License expiring within 12 months
- **WHEN** today is 2026-10-02 and an arquebusier's AE license has `expiresOn` 2027-10-01
- **THEN** the license status is `VALID` and the arquebusier has the warning `LICENSE_EXPIRING`

#### Scenario: License expiring in exactly 12 months
- **WHEN** today is 2026-10-02 and an arquebusier's license has `expiresOn` 2027-10-02
- **THEN** the arquebusier has no license warning

#### Scenario: License expiring today
- **WHEN** today is 2026-10-02 and an arquebusier's license has `expiresOn` 2026-10-02
- **THEN** the license status is `VALID` and the arquebusier has the warning `LICENSE_EXPIRING`

#### Scenario: Pending license
- **WHEN** an arquebusier has a pending license without photos
- **THEN** the arquebusier has the warning `LICENSE_PENDING` and not `LICENSE_PHOTOS_MISSING`

#### Scenario: No license
- **WHEN** an arquebusier has no license
- **THEN** the arquebusier has the warning `LICENSE_MISSING` and not `LICENSE_PHOTOS_MISSING`

#### Scenario: Missing back photo of an issued license
- **WHEN** an arquebusier's issued license has a `frontPhoto` and no `backPhoto`
- **THEN** the arquebusier has the warning `LICENSE_PHOTOS_MISSING`

#### Scenario: Course not done
- **WHEN** an arquebusier has no `trainingCompletedOn`
- **THEN** the arquebusier has the warning `COURSE_MISSING`

#### Scenario: Eighteenth birthday
- **WHEN** today is 2026-10-02 and one arquebusier was born on 2008-10-02 and another on 2008-10-03
- **THEN** the first has no `UNDER_AGE` warning and the second has it

#### Scenario: Missing ID photo
- **WHEN** an arquebusier has no `idPhoto`
- **THEN** the arquebusier has the warning `ID_PHOTO_MISSING`

#### Scenario: Reserve arquebusiers are checked too
- **WHEN** an arquebusier with status `RESERVE` has an expired license
- **THEN** the arquebusier has the warning `LICENSE_EXPIRED`

#### Scenario: Warnings never block
- **WHEN** a FiringChief edits the phone of an arquebusier of their comparsa who has the warnings `LICENSE_EXPIRED`, `COURSE_MISSING` and `UNDER_AGE`
- **THEN** the edit is saved, and the arquebusier still has the three warnings

#### Scenario: Warnings follow the date
- **WHEN** an arquebusier's license has `expiresOn` 2027-10-01 and the date moves from 2026-10-01 to 2026-10-02 in Europe/Madrid
- **THEN** the arquebusier has no license warning on 2026-10-01 and the warning `LICENSE_EXPIRING` on 2026-10-02, without any change to the arquebusier

#### Scenario: Compliant arquebusier
- **WHEN** an arquebusier is 30 years old, has the course done, an ID photo, and an issued license with both photos that expires in two years
- **THEN** the arquebusier has no warning

### Requirement: Warning summary
The API SHALL return, for a signed-in user, a summary of the arquebusiers within their scope (BR-12):
- the number of `ACTIVE` and of `RESERVE` arquebusiers;
- the number of arquebusiers with at least one warning;
- for every warning, the number of arquebusiers that have it, including the warnings no one has.

An arquebusier with several warnings SHALL be counted once in the number with warnings and once
for each of their warnings. An Admin's summary SHALL cover every comparsa. A FiringChief's summary
SHALL cover only the comparsas assigned to them, and SHALL be all zeros when they have none. The
summary SHALL contain no data that identifies an arquebusier. A request without a session SHALL be
answered `401 Unauthorized`.

#### Scenario: FiringChief summary
- **WHEN** the seeded FiringChief "Jefa Sintética Dos" requests the summary
- **THEN** the figures count only the arquebusiers of the comparsas assigned to her

#### Scenario: Admin summary
- **WHEN** an Admin requests the summary
- **THEN** the figures count the arquebusiers of every comparsa, active or inactive

#### Scenario: Several warnings count once as an arquebusier
- **WHEN** the only arquebusier in a FiringChief's scope has the warnings `COURSE_MISSING` and `UNDER_AGE`
- **THEN** the summary has one arquebusier with warnings, one for `COURSE_MISSING`, one for `UNDER_AGE` and zero for every other warning

#### Scenario: FiringChief without assignments
- **WHEN** a FiringChief with no comparsa assigned requests the summary
- **THEN** every figure is zero

#### Scenario: Signed-out request
- **WHEN** a request without a session asks for the summary
- **THEN** the API responds `401 Unauthorized`

### Requirement: Alerts dashboard (UC-06)
The start page SHALL be the alerts dashboard for every signed-in user, following the dashboard
template. Within the user's scope, it SHALL show:
- figures for the active arquebusiers, the reserve arquebusiers and the arquebusiers with
  warnings, each linking to the arquebusier list with the matching filter;
- one figure for each warning, with its count, linking to the arquebusier list filtered by that
  warning;
- a warning message that says how many arquebusiers need attention, when at least one has a
  warning. When none has a warning, a message SHALL say that every arquebusier is up to date;
- a short table of the next license expiries: up to 10 arquebusiers with the warning
  `LICENSE_EXPIRING`, sorted by `expiresOn` and then by name. Each row SHALL show the name, the
  comparsa and the expiry date, SHALL link to the arquebusier, and the table SHALL link to the full
  filtered list.

A FiringChief without assigned comparsas SHALL see an empty state that explains that no comparsa
is assigned to them yet, instead of the figures. When the data cannot be loaded, the page SHALL
say so with a retry action. Figures SHALL be formatted in the user's language. The page SHALL work
on a phone (NFR-01), SHALL have no automatically detectable WCAG 2.2 AA violations in either theme
(NFR-07), and every text SHALL be available in es-ES, ca-ES-valencia and en.

#### Scenario: FiringChief opens the start page
- **WHEN** the seeded FiringChief "Jefa Sintética Dos" signs in
- **THEN** the start page shows the figures for the arquebusiers of her comparsas only, including one figure per warning

#### Scenario: Figure opens the filtered list
- **WHEN** a user chooses the `COURSE_MISSING` figure on the start page
- **THEN** the arquebusier list opens filtered by the `COURSE_MISSING` warning, and lists only the arquebusiers without the course

#### Scenario: Next license expiries
- **WHEN** an Admin opens the start page while 12 arquebusiers have the warning `LICENSE_EXPIRING`
- **THEN** the table shows the 10 that expire first, in order of expiry, with a link to the list of all of them

#### Scenario: Everyone up to date
- **WHEN** a FiringChief whose arquebusiers have no warnings opens the start page
- **THEN** the page says that every arquebusier is up to date, and every warning figure shows zero

#### Scenario: No comparsa assigned
- **WHEN** a FiringChief with no comparsa assigned opens the start page
- **THEN** an empty state explains that no comparsa is assigned to them yet, and no figure is shown

#### Scenario: Summary unavailable
- **WHEN** the summary request fails
- **THEN** the start page shows a translated message with a retry action, and the shell still works

### Requirement: Warning count in the navigation
The Arquebusiers item of the main navigation SHALL show the number of arquebusiers with at least one
warning within the user's scope. The count SHALL be part of the item's accessible name, for example
"Arquebusiers, 5 with warnings". It SHALL NOT be shown when the number is zero or cannot be loaded.
After a change to an arquebusier, the count SHALL be updated without reloading the page.

#### Scenario: Count shown
- **WHEN** a FiringChief whose comparsa has 3 arquebusiers with warnings is signed in
- **THEN** the Arquebusiers item shows 3, and a screen reader announces it with the item's name

#### Scenario: No count when nothing needs attention
- **WHEN** no arquebusier in the user's scope has a warning
- **THEN** the Arquebusiers item shows no count

#### Scenario: Count follows a fix
- **WHEN** the only arquebusier with a warning in a FiringChief's scope gets the course date recorded and has no other warning
- **THEN** the navigation count disappears without reloading the page

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
- the gender figures across age brackets, course and owned weapons SHALL form the equality report,
  shown as tables whose rows and columns are labelled for assistive technology;
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

### Requirement: Insights are read-only
Viewing the dashboard, the warnings or the statistics SHALL change no data and SHALL record no
audit entry, because nothing is written or exported (SEC-05 covers writes and exports). Insights
SHALL NOT be cached by the browser or any intermediary, like every API response. Server logs of
insight requests SHALL NOT contain personal data.

#### Scenario: Viewing records nothing
- **WHEN** a FiringChief opens the start page and the Statistics page
- **THEN** no data changes and no audit entry is recorded
