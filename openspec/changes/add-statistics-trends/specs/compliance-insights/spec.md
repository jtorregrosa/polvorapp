# Spec Delta

## MODIFIED Requirements

### Requirement: Statistics screen
The UI SHALL offer a Statistics page in the main navigation to every signed-in user. It SHALL have
two tabs, "Today" (the registry as it is now, described below) and "Trends" (see "Trends screen"),
with the chosen tab kept in the page address and "Today" by default. The "Today" tab SHALL follow
the dashboard template:
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

#### Scenario: Tabs kept in the address
- **WHEN** a user opens the "Trends" tab of the Statistics page and reloads the page
- **THEN** the "Trends" tab is still selected, and the comparsa filter is kept across both tabs

## ADDED Requirements

### Requirement: Edition trends (UC-07)
The API SHALL return, for the arquebusiers' participation within the user's scope (BR-12), one row
per festival edition that is not `DRAFT`, at most the 10 most recent by year, sorted by year. It
SHALL optionally be filtered by one comparsa. Each row SHALL count the entries of the prepared
orders of that edition (whatever the order's status) and SHALL include:
- the edition's year and whether its figures are provisional (the edition is `IN_PROGRESS`);
- the number of `ACTIVE` and `RESERVE` entries;
- the number of `ACTIVE` entries of each `Gender`, taken from the registry today, and of those whose
  arquebusier is no longer in the registry (`UNKNOWN`);
- the number of `ACTIVE` entries in their first year (no `ACTIVE` entry in an earlier edition), or
  unknown for the first edition with orders;
- the total `powderKg` and `capsBoxes`;
- the number of `ACTIVE` entries by weapon source (`OWNED`, `RENTAL`, `LOAN`, `NONE`), the rentals
  by `WeaponKind`, and the flask rentals;
- when the user sees more than one comparsa and no comparsa filter is set: for each comparsa in
  scope, its number of `ACTIVE` entries in that edition.

The response SHALL contain only counts, years and comparsa names, never data of an arquebusier.
These rules SHALL be blocking: a comparsa outside the user's scope or that does not exist SHALL be
answered `404 Not Found`. Reading trends SHALL NOT be audited, like the other statistics.

#### Scenario: FiringChief trends
- **WHEN** a FiringChief assigned to one comparsa requests the trends
- **THEN** every row counts only the entries of that comparsa, and no per-comparsa figures are returned

#### Scenario: Draft editions are left out
- **WHEN** an Admin requests the trends while editions 2025 and 2026 are `CLOSED`, 2027 is `IN_PROGRESS` and 2028 is `DRAFT`
- **THEN** the response has rows for 2025, 2026 and 2027, sorted by year, and only 2027 is marked provisional

#### Scenario: Gender of a deleted arquebusier
- **WHEN** an `ACTIVE` entry of 2026 belongs to an arquebusier deleted from the registry since
- **THEN** the 2026 row counts that entry under `UNKNOWN` gender, and the response holds no name or identifier

#### Scenario: First edition with orders
- **WHEN** the oldest edition in the response is the first one with orders
- **THEN** its first-year figure is marked unknown instead of counting every arquebusier

#### Scenario: Comparsa outside the scope
- **WHEN** a FiringChief requests the trends of a comparsa they are not assigned to
- **THEN** the API responds `404 Not Found`

### Requirement: Trends screen
The "Trends" tab of the Statistics page SHALL show, for the editions returned by "Edition trends",
each as a chart with a heading, a one-sentence summary of the latest edition against the previous
one (for example "2027: 412 active, 3 % more than 2026"), and the same data as a table:
- `ACTIVE` and `RESERVE` arquebusiers per edition;
- the share of women among `ACTIVE` arquebusiers per edition, with the `UNKNOWN` count stated;
- first-year arquebusiers per edition;
- powder (kg) and caps (boxes) per edition;
- weapon source of `ACTIVE` arquebusiers per edition (owned, rental, loan, none), and rentals by
  weapon kind;
- for Admins and FiringChiefs with several comparsas, without a comparsa filter: a table of `ACTIVE`
  arquebusiers per comparsa and edition, with the change against the previous edition, sortable by
  every column.

Provisional figures SHALL be marked as such in the chart, the summary and the table. With fewer
than two editions, the tab SHALL say that trends need at least two editions and SHALL still show the
one edition's figures as a table. The tab SHALL follow "Charts", SHALL work on a phone (NFR-01),
SHALL have no automatically detectable WCAG 2.2 AA violations in either theme (NFR-07), and every
text SHALL be available in es-ES, ca-ES-valencia and en.

#### Scenario: Arquebusiers per edition
- **WHEN** an Admin opens the "Trends" tab with closed editions 2025 and 2026 and the 2027 edition in progress
- **THEN** a chart shows the active and reserve arquebusiers of 2025, 2026 and 2027, 2027 marked provisional, with a summary sentence and a table of the same figures

#### Scenario: Only one edition
- **WHEN** a user opens the "Trends" tab while only one edition has orders
- **THEN** the tab says that trends need at least two editions and shows that edition's figures as a table, without charts

#### Scenario: FiringChief trends screen
- **WHEN** a FiringChief assigned to one comparsa opens the "Trends" tab
- **THEN** the charts show only their comparsa and no per-comparsa table is shown

#### Scenario: Trends on a phone
- **WHEN** a user opens the "Trends" tab on a 360 px wide screen
- **THEN** each chart fits the width, its table can be read, and the page does not scroll horizontally
