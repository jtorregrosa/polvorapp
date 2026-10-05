# Spec Delta

## MODIFIED Requirements

### Requirement: Motion
Motion SHALL use the duration and easing tokens and SHALL only animate opacity and transforms,
never layout. Each motion has a ceiling:
- colour and border changes: 100 ms;
- menus and pickers: 150 ms to appear;
- dialogs: 200 ms to appear;
- side panels, bottom sheets and the navigation drawer: 250 ms to appear;
- the sidebar collapsing to its icon rail and expanding back: 200 ms each way.

The sidebar is the one exception to "never layout": its width, and the offset of the content beside
it, SHALL be animated together over 200 ms with the drawer easing, whether toggled from its button
or its keyboard shortcut. Labels SHALL fade out before the rail is reached and fade in after the
width is restored, so text never wraps mid-animation. Input SHALL stay possible during the
animation.

Leaving SHALL be shorter than appearing. Route changes, sorting, filtering, paging, validation
messages, theme and language changes, and changes triggered by a keyboard shortcut (other than the sidebar's) SHALL NOT be
animated. Decorative motion (looping animations, staggered lists, hover scaling of rows) SHALL NOT
be used. When the user asks for reduced motion, movement and scaling SHALL be removed and only fades
of at most 100 ms SHALL remain; the sidebar SHALL then change width at once. Animations SHALL never block input.

#### Scenario: Drawer on a phone
- **WHEN** a user opens the navigation drawer on a 360 px screen
- **THEN** it slides in within 250 ms and can be used during the animation

#### Scenario: Reduced motion
- **WHEN** a user whose system asks for reduced motion opens a side panel
- **THEN** the panel appears with a fade of at most 100 ms and without sliding

#### Scenario: Keyboard toggle
- **WHEN** a user collapses the sidebar with its keyboard shortcut
- **THEN** the sidebar narrows to its icon rail over 200 ms, the content beside it widens with it, and the labels fade out first

#### Scenario: Sidebar with reduced motion
- **WHEN** a user whose system asks for reduced motion expands the sidebar
- **THEN** it takes its full width at once, and only the labels fade in within 100 ms

### Requirement: Data tables
Tabular lists SHALL support sorting by column, pagination with a translated summary, a loading
state and an empty state with a translated message. A row MAY show a second line with secondary
data. When a row leads to a record, the row's name SHALL be a link, and pointing or tapping anywhere
on the row SHALL open the record too. A cell MAY hold its own link to a related record (for example
the comparsa of an arquebusier); that link SHALL open its own target, and the rest of the row SHALL
still open the row's record. Every column whose values can be ordered SHALL be sortable, by the
value it shows; columns of actions SHALL NOT be. A missing value SHALL be shown in the muted text
colour, and a lone dash SHALL carry a text for screen readers saying what is missing. On screens narrower than the phone breakpoint, each row SHALL
be shown as a stacked item with the name, the identifier and the main status, and the page SHALL
NOT scroll horizontally. On wider screens, a table wider than its container SHALL scroll inside the
container. Row hover SHALL change the background at once, without a transition.

A table MAY offer row selection. When it does:
- each row, and each stacked item on phones, SHALL have a checkbox labelled with the row's name;
- the header SHALL have a checkbox that selects or clears every row of the current page, shown as
  mixed when only some of them are selected;
- the selection SHALL be kept across pages, sorting and filtering, and SHALL be owned by the
  screen, which can set, read and clear it;
- the number of selected rows SHALL be announced to screen readers when it changes;
- choosing a checkbox SHALL NOT open the row's record, and a selected row SHALL be shown as
  selected by more than colour alone.

#### Scenario: Empty list
- **WHEN** a table has no rows
- **THEN** a translated empty-state message is shown instead of an empty grid

#### Scenario: Narrow screen
- **WHEN** a table wider than its container is shown on a screen between the phone breakpoint and the table's width
- **THEN** the table scrolls horizontally inside its container and the page does not

#### Scenario: Stacked rows on a phone
- **WHEN** the arquebusiers list is shown on a 360 px screen
- **THEN** each arquebusier is a stacked item with their name, nationalId, comparsa and license status, and the page does not scroll horizontally

#### Scenario: Whole row opens the record
- **WHEN** a user clicks the license cell of an arquebusier row
- **THEN** that arquebusier's detail page opens

#### Scenario: Link inside a row
- **WHEN** a user clicks the comparsa link in an arquebusier row
- **THEN** that comparsa's page opens, not the arquebusier's

#### Scenario: Sort by a shown value
- **WHEN** an Admin sorts the users table by two-step verification
- **THEN** the rows are ordered by the translated value shown in that column, and the change is announced

#### Scenario: Keyboard opens the record
- **WHEN** a keyboard user tabs to a row and activates its name link
- **THEN** that arquebusier's detail page opens, and the row adds no extra tab stop

#### Scenario: Selecting a row does not open it
- **WHEN** a user clicks the checkbox of a row in a table with row selection
- **THEN** the row is selected, the record does not open, and the selected count is announced

#### Scenario: Select the page
- **WHEN** a user ticks the header checkbox on a page of 20 rows where 3 were already selected
- **THEN** all 20 rows of the page are selected, rows selected on other pages stay selected, and ticking it again clears the 20 rows of the page only

#### Scenario: Selection on a phone
- **WHEN** a table with row selection is shown on a 360 px screen
- **THEN** each stacked item has its labelled checkbox, and the page does not scroll horizontally

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
- two-step verification of a user: `ENABLED` → success, `NOT_SET` → muted;
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

#### Scenario: Two-step verification not set
- **WHEN** the users table shows an invited user who has not enrolled an authenticator
- **THEN** the two-step verification column shows a muted badge with the translated "not set" label and its icon

## ADDED Requirements

### Requirement: Tags for fixed values
The composite layer SHALL offer a tag for categorical values that are not statuses: a value from a
fixed list that says what something is, not how it is doing (for example a role, a side, a weapon
kind, a yes/no flag). A tag SHALL show a translated label on a tinted background from categorical
tokens, and SHALL NOT use the semantic colours (success, warning, destructive, info) or a status
icon, so it is never read as a status. Each value of a list SHALL keep the same tone wherever it is
shown. The meaning SHALL never rely on the tone alone: the label is always present. Every tone SHALL
meet WCAG 2.2 AA text contrast in both themes (NFR-07). The categorical tokens SHALL be defined for
the light and dark themes, and a "no" or "none" value SHALL use the neutral tone.

These fixed values SHALL be shown as tags in lists and detail pages:
- `UserRole`: `ADMIN` and `FIRING_CHIEF`;
- `Side`: `MOORISH` and `CHRISTIAN`;
- `WeaponKind`: `TRABUCO`, `ARCABUZ` and `PISTOL`;
- the rentable flag of a weapon model: yes in a tone, no in the neutral tone.

#### Scenario: Side tag
- **WHEN** the comparsas list shows a Moorish and a Christian comparsa in Valencian
- **THEN** each side is a tag with its Valencian label, in two different categorical tones, without a status icon

#### Scenario: Tag contrast
- **WHEN** every tag tone is checked against its background in the light and dark themes
- **THEN** each pair reaches at least 4.5:1

#### Scenario: Not rentable
- **WHEN** the weapon catalogue shows a model that is not rentable
- **THEN** its rentable column shows a neutral "No" tag

### Requirement: Picture actions
The composite layer SHALL offer a way to manage a single picture from the picture itself, used for
the comparsa logo and the Federation logo. When there is a picture, the picture SHALL be a button
named after it (for example "Logo of Comparsa Norte, options") that opens a menu with "Replace" and
"Remove". When there is none, the placeholder SHALL be an "Add" button. Choosing "Replace" or "Add"
SHALL continue as in "Photo upload with cropping", and "Remove" SHALL follow "Confirmation of
destructive actions". On pointer hover and on keyboard focus, an overlay with an edit icon SHALL
show that the picture can be changed; the overlay SHALL NOT be the only way to know it (the button
has a name). The button SHALL be at least 44 × 44 px, SHALL work with the keyboard, pointer and
touch alike, and SHALL return focus to itself when the menu, the crop dialog or the confirmation
closes. Progress and results SHALL be announced as in "Photo upload with cropping".

#### Scenario: Replace from the picture
- **WHEN** an Admin activates the comparsa logo with the keyboard and chooses "Replace"
- **THEN** the file chooser and then the crop dialog open, and after uploading, focus returns to the logo

#### Scenario: Add from the placeholder
- **WHEN** an Admin activates the placeholder of a comparsa without a logo
- **THEN** the file chooser opens, without a menu

#### Scenario: Touch
- **WHEN** an Admin taps the logo on a phone
- **THEN** the menu opens, without relying on hover

#### Scenario: Remove asks first
- **WHEN** an Admin chooses "Remove" in the logo menu and cancels the confirmation
- **THEN** the logo is kept and focus returns to it
