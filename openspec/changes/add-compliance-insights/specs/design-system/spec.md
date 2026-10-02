# Spec Delta

## MODIFIED Requirements

### Requirement: Status semantics
Every domain status SHALL be rendered by one status component that always shows a translated text
label together with a semantic colour and an icon, never colour alone. The mapping SHALL cover:
- `LicenseStatus`: `VALID` → success, `EXPIRED` → destructive, `PENDING` → info. The derived
  "expiring soon" state of a `VALID` license with the compliance warning `LICENSE_EXPIRING` →
  warning;
- `ArquebusierStatus`: `ACTIVE` → success, `RESERVE` → muted;
- `ComparsaOrder` status: `DRAFT` → muted, `SUBMITTED` → info, `RETURNED` → warning, `VALIDATED` →
  success;
- `FestivalEdition` status: `DRAFT` and `CLOSED` → muted, `ORDERS_OPEN` → success,
  `CORRECTIONS_OPEN` → warning, `LOCKED` → info;
- compliance warnings (BR-04): `LICENSE_MISSING`, `LICENSE_PENDING`, `LICENSE_EXPIRED`,
  `LICENSE_EXPIRING`, `COURSE_MISSING`, `UNDER_AGE`, `ID_PHOTO_MISSING` and
  `LICENSE_PHOTOS_MISSING` → warning.

Compliance warnings SHALL never use the destructive colour, because they do not block (BR-04 is a
warning).

#### Scenario: Expired license
- **WHEN** a license with status `EXPIRED` is rendered in Valencian
- **THEN** the badge shows the Valencian label for "expired", the destructive colour and an icon

#### Scenario: Compliance warning is not an error
- **WHEN** a missing-course warning (BR-04) is rendered
- **THEN** it uses the warning colour and icon, not the destructive ones

#### Scenario: Expired-license warning is not an error
- **WHEN** the `LICENSE_EXPIRED` warning is rendered
- **THEN** it uses the warning colour and icon, while the `EXPIRED` license status keeps the destructive colour

#### Scenario: Unknown status value
- **WHEN** a status value outside the mapping is rendered
- **THEN** a neutral badge with the raw code is shown and the case is reported in development builds

## ADDED Requirements

### Requirement: Breakdown figures
The composites layer SHALL offer a breakdown component for dashboards. It SHALL show a titled
table of labelled categories, each with:
- its count;
- its share of a given total, as a whole percentage;
- a bar that represents the share.

Counts and percentages SHALL be formatted in the user's language and right-aligned. The percentage
SHALL always be shown as text, so the share is never given by the bar or its colour alone. The bar
SHALL be a native meter that only repeats the text visually. It SHALL be hidden from assistive
technology, so the share is not announced twice. The bar SHALL use only design tokens, and it SHALL
stay within its range when a share exceeds 100 %. A breakdown SHALL also accept several count
columns per category (for example one per gender), with labelled column headers. With a total of
zero it SHALL show the counts without percentages or bars. The component SHALL be keyboard and
screen-reader usable, SHALL fit a 360 px wide screen by letting its table scroll inside its own
area, SHALL have no automatically detectable WCAG 2.2 AA violations in either theme, and SHALL have
a catalogue story.

#### Scenario: Share as text and bar
- **WHEN** a breakdown shows 3 women out of a total of 12 in Spanish
- **THEN** the row shows "3" and "25 %" as text, and a meter at 25 that assistive technology does not announce

#### Scenario: Several columns
- **WHEN** a breakdown shows age brackets with one column per gender
- **THEN** every cell is associated with its row and column headers for assistive technology

#### Scenario: Zero total
- **WHEN** a breakdown's total is zero
- **THEN** the counts are shown and no percentage or bar is rendered

#### Scenario: Narrow screen
- **WHEN** a breakdown with four count columns is shown on a 360 px wide screen
- **THEN** the page does not scroll horizontally, and the breakdown's own area scrolls instead
