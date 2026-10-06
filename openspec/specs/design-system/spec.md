# design-system Specification

## Purpose

Gives every PolvorApp screen one coherent, accessible and themeable look through closed design
tokens, a layer of PolvorApp composites, shared status semantics, a component catalogue and the
guardrails that stop feature code from bypassing them (ADR-0009, ADR-0013).

## Requirements

### Requirement: PolvorApp visual identity
The UI SHALL present PolvorApp's own identity in the application, the browser tab and the installed
app: its name, its own mark, its own palette and its own typefaces. Titles and key figures SHALL use
a display typeface, the interface a text typeface and identifiers (nationalId, federationId, guide
numbers) a monospaced typeface. All of them SHALL be served by the application itself under a
licence that allows redistribution. The navigation sidebar SHALL keep its dark "night" surface in
both themes, with the brand accent as the only accent colour. The repository SHALL NOT contain logos
or brand assets of the Federation or any other organisation (ADR-0013).

#### Scenario: Installed application
- **WHEN** a user installs the application on a phone or desktop
- **THEN** it is shown with the name "PolvorApp", the PolvorApp mark and the theme colour defined by the design tokens

#### Scenario: Browser tab
- **WHEN** a user opens any page
- **THEN** the tab shows the PolvorApp favicon and a title ending in "PolvorApp"

#### Scenario: Fonts are self-hosted
- **WHEN** a user opens any page with network access to third-party hosts blocked
- **THEN** the titles, the interface text and the identifiers are rendered in the PolvorApp typefaces, and no font is requested from another origin

#### Scenario: Sidebar in the light theme
- **WHEN** a user with the light theme opens any signed-in page
- **THEN** the navigation sidebar has the dark surface, and its text, icons, counters and focus indicator meet their contrast minimums against it

### Requirement: Design tokens with light and dark themes
All colours, font families, font sizes and weights, line heights, spacing, control and row heights,
radii, elevations, page widths, breakpoints, durations and easings used by the UI SHALL come from
one set of design tokens defined for a light and a dark theme. Colours SHALL be semantic
(`background`, `foreground`, `primary`, `muted`, `border`, `success`, `warning`, `destructive`,
`info`, …), and SHALL include the hover and pressed states of filled controls, so no state is
produced by lowering a token's opacity. Type sizes SHALL be semantic roles (page title, record name,
section title, key figure, body, label, help and identifier), and SHALL step up on screens at least
1700 px wide. Spacing SHALL follow one rhythm in which the gap grows from field to group to section
to page. In both themes, every text token SHALL reach a contrast of at least 4.5:1 against the
surfaces it is used on, including in its hover and pressed states. Interactive boundaries and the
focus indicator SHALL reach at least 3:1 (WCAG 2.2 AA, NFR-07).

#### Scenario: Contrast verified
- **WHEN** the token contrast checks run
- **THEN** every declared text/surface, state/surface and indicator/surface pair meets its minimum ratio in both themes

#### Scenario: Insufficient contrast
- **WHEN** a token is changed so that a declared pair falls below its minimum ratio
- **THEN** the contrast check fails naming the theme, the pair and the measured ratio

#### Scenario: Hover keeps contrast
- **WHEN** a user points at the primary button in either theme
- **THEN** its label keeps at least 4.5:1 against the hover colour

#### Scenario: Type steps up on wide screens
- **WHEN** a page is shown in a window at least 1700 px wide
- **THEN** body text, page titles and record names use their larger token sizes

### Requirement: Theme preference
The UI SHALL follow the operating system's light or dark preference by default, and SHALL let the
user choose light, dark or system from a translated switcher. An explicit choice SHALL be
remembered in the browser and applied before the first paint on later visits. When storage is
unavailable the switch SHALL still apply for the session.

#### Scenario: System preference
- **WHEN** a first-time user whose system prefers dark opens the application
- **THEN** the dark theme is applied

#### Scenario: Explicit choice remembered
- **WHEN** a user chooses the light theme and reloads the page
- **THEN** the light theme is applied without first showing the dark theme

### Requirement: Component layers and boundary
UI building blocks SHALL live in two layers: generic primitives and PolvorApp composites. Feature
code SHALL use only the composites; importing a primitive from feature code SHALL fail the lint
check. Composites may use primitives.

#### Scenario: Feature imports a primitive
- **WHEN** a file under `src/features/` imports from the primitives layer
- **THEN** the lint check fails naming the file and the import

#### Scenario: Composite uses a primitive
- **WHEN** a composite imports a primitive
- **THEN** the lint check passes

### Requirement: No values outside the tokens
UI code outside the token definitions SHALL NOT use arbitrary style values (for example
`mt-[13px]`, `text-[#c00]`), raw palette colours (for example `text-red-500`) or inline `style`
attributes for visual design. Violations SHALL fail the lint check.

#### Scenario: Arbitrary value
- **WHEN** a component uses the class `p-[7px]`
- **THEN** the lint check fails

#### Scenario: Raw palette colour
- **WHEN** a feature uses the class `bg-emerald-600` instead of a semantic token
- **THEN** the lint check fails

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
- **THEN** the second line of the user's status shows a muted two-step verification badge with the translated "not set" label and its icon

### Requirement: Accessible composites
Every composite SHALL be operable with the keyboard alone and SHALL expose correct names, roles and
states to assistive technology. It SHALL have no automatically detectable WCAG 2.2 A/AA violations
in either theme (NFR-07). Focus SHALL be shown by an indicator that:
- is at least 2 px thick;
- reaches 3:1 against its surroundings;
- is drawn at full opacity;
- is shown on menu and option items too, not as a background change alone.

Pointer targets SHALL be at least 24 × 24 px, and controls on touch screens at least 44 px high.
Dialogs, side panels, bottom sheets and the navigation drawer SHALL move focus inside, keep it
there, close with Escape and return focus to the element that opened them. Menus SHALL close with
Escape and return focus to their trigger. Sticky bars SHALL never cover the focused control
(SC 2.4.11).

#### Scenario: Dialog focus
- **WHEN** a keyboard user opens a confirmation dialog and presses Escape
- **THEN** the dialog closes and focus returns to the button that opened it

#### Scenario: Automated checks
- **WHEN** the automated accessibility checks run over every composite in both themes
- **THEN** no WCAG 2.2 A or AA violations are reported, target-size checks included

#### Scenario: Focus indicator contrast
- **WHEN** a keyboard user moves focus to a button, a text field, a select, a checkbox, a radio card or a menu item in either theme
- **THEN** a focus indicator of at least 2 px is shown with at least 3:1 contrast against the surrounding colours

#### Scenario: Focus not hidden by a sticky bar
- **WHEN** a keyboard user tabs through a long form whose action bar stays fixed at the bottom of the screen
- **THEN** each focused field is scrolled into view above the action bar, not behind it

#### Scenario: Small close button
- **WHEN** a dialog or side panel is shown
- **THEN** its close button is at least 24 × 24 px

### Requirement: Confirmation of destructive actions
Destructive or irreversible actions SHALL require confirmation in a dialog that names the action
and its object, labels the confirming button with the action itself (not "OK"), styles it as
destructive and places initial focus on the cancelling option.

#### Scenario: Confirm deletion
- **WHEN** a user triggers a destructive action
- **THEN** a dialog asks for confirmation with a button labelled with the action and the cancel option focused

#### Scenario: Cancel
- **WHEN** the user cancels or presses Escape
- **THEN** nothing is changed

### Requirement: Form fields and validation messages
Every form field SHALL show a visible label, then optional help text, then the error message when
there is one, then the control. Fields SHALL be required unless their label says "(optional)" in the
user's language. Each form SHALL state this once, at its start, and SHALL NOT use a required marker
such as an asterisk. A field's width SHALL match the content it expects: short for identifiers,
dates and phone numbers, medium for names and email addresses. Only free text SHALL take the full
width. A choice among two to four options SHALL be offered as radio options with their label and
any help text. A longer or growing list SHALL use a select. A field that applies only after an
answer SHALL appear under that answer when it applies, instead of being shown disabled.

Validation SHALL run when the form is submitted, and then again whenever a field that has an error
changes. When a submission has errors, the form SHALL:
- show an error summary at its top, titled as a problem, that lists each error as a link to its
  field;
- move focus to the summary;
- repeat each translated error at its field, programmatically associated with the field and marked
  by a bar beside the field, not by colour alone.

Compliance checks (license, course, age) SHALL NOT appear as errors, because they never block
(BR-04).

#### Scenario: Invalid field
- **WHEN** a user submits a form with an invalid required field
- **THEN** the field is marked invalid by a bar and its translated error, the error is announced with the field, and the error summary that receives focus links to it

#### Scenario: Optional field
- **WHEN** a user opens the arquebusier register form in Valencian
- **THEN** the email and phone labels end with the Valencian word for "optional", no label carries an asterisk, and one sentence at the start says that the other fields are required

#### Scenario: Failed submission
- **WHEN** a user submits the register form with the nationalId and the birth date empty
- **THEN** an error summary listing both problems appears at the top of the form and receives focus, and each field shows its own error

#### Scenario: Error summary link
- **WHEN** the user activates the summary link of the birth date error
- **THEN** focus moves to the birth date field, which is scrolled into view

#### Scenario: Revalidation after a fix
- **WHEN** the user corrects the nationalId after a failed submission
- **THEN** that field's error disappears without submitting again

#### Scenario: Conditional field
- **WHEN** a user chooses the license type `AE` on the register form
- **THEN** the pending option and the issue and expiry dates appear under that answer, and they are not shown while "no license" is chosen

#### Scenario: Short choice as radio options
- **WHEN** a user reaches the gender field
- **THEN** the three genders are shown as radio options, not as a select

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

### Requirement: Component catalogue
Every composite SHALL have an entry in the component catalogue showing its states in the three UI
languages and both themes. The build pipeline SHALL fail when a composite has no catalogue entry or
when any catalogue entry has an accessibility violation.

#### Scenario: Composite without catalogue entry
- **WHEN** a new composite is added without a catalogue entry
- **THEN** the pipeline fails naming the composite

### Requirement: Design guide
The repository SHALL contain a design guide describing:
- the principles and the identity;
- the tokens (colour, type, spacing, elevation, widths and motion);
- the status semantics;
- the page templates (list, detail in read mode, form, dashboard);
- the form rules and error patterns;
- the action hierarchy and destructive actions;
- the empty and loading states;
- the responsive rules from 360 to 2560 px;
- the motion rules;
- the microcopy tone in the three languages.

It SHALL be referenced from the project context used for new changes.

#### Scenario: New change context
- **WHEN** a contributor or agent starts a new change
- **THEN** the project context points to the design guide as the UI reference

#### Scenario: Templates documented
- **WHEN** a contributor builds a new detail page
- **THEN** the guide describes the detail template, its composites and its responsive behaviour from 360 to 2560 px

### Requirement: Photo upload with cropping
The composite layer SHALL offer a photo upload control for feature screens. It SHALL show the
current photo with a text alternative that describes it, or an empty state when there is none. It
SHALL let the user:
- choose an image file;
- on a phone, take a picture with the camera or pick one from the gallery (NFR-01);
- crop the image in a dialog, with a fixed shape when the screen asks for one;
- rotate the image by quarter turns;
- preview the result before confirming.

Without a fixed shape, the crop area SHALL start with the whole image selected. When the screen asks
to keep transparency (for example for logos), the control SHALL keep the transparent areas of the
image through decoding, rotation, cropping and the preview, and SHALL upload a PNG; otherwise it
SHALL upload a JPEG.

It SHALL check the format, the size and the minimum dimensions that the screen asks for before
anything is uploaded, and SHALL explain any problem in the user's language. It SHALL upload only
the cropped image, never the original file, and SHALL announce when the upload is in progress, has
succeeded or has failed. Every part SHALL be operable with the keyboard alone, including moving and
resizing the crop area. Moving and resizing the crop area SHALL also be possible with single
pointer activations (buttons), without dragging (SC 2.5.7). The control SHALL meet the "Accessible
composites" requirement. A photo that cannot be loaded SHALL be replaced by a message, never a
broken image. Removing a photo SHALL follow the "Confirmation of destructive actions" requirement.

#### Scenario: Crop with the keyboard
- **WHEN** a keyboard user opens the crop dialog for a 3:4 photo, moves and resizes the crop area with the arrow keys, and confirms
- **THEN** the cropped 3:4 image is handed to the screen and focus returns to the control that opened the dialog

#### Scenario: Crop without dragging
- **WHEN** a user moves the crop area and makes it smaller using only the dialog's buttons
- **THEN** the crop area moves and shrinks by one step per activation, keeping its fixed shape, and the result can be confirmed

#### Scenario: Free crop starts with the whole image
- **WHEN** a user chooses an image for a control without a fixed shape
- **THEN** the crop dialog opens with the whole image selected, and confirming at once hands the whole image to the screen

#### Scenario: Transparency kept
- **WHEN** a user chooses a PNG with a transparent background for a control that keeps transparency, rotates it and confirms
- **THEN** the screen receives a PNG whose background is still transparent, and the preview shows the transparency

#### Scenario: Image too small
- **WHEN** a user chooses an image smaller than the minimum dimensions the screen asks for
- **THEN** the control explains that the image is too small, and nothing is uploaded

#### Scenario: Unreadable file
- **WHEN** a user chooses a file that the browser cannot open as an image
- **THEN** the control explains that the format is not supported, and nothing is uploaded

#### Scenario: Photo fails to load
- **WHEN** the current photo cannot be loaded
- **THEN** the control shows a translated message in its place instead of a broken image

#### Scenario: Catalogue entry
- **WHEN** the component catalogue is built
- **THEN** it shows the photo upload control empty, with a photo, with a transparent logo, disabled with its reason and with an error, in both themes and the three UI languages

### Requirement: Page templates
Every signed-in page SHALL use one of the templates:
- **list**: a title with its main action, then a filter bar, then the table;
- **detail**: a record header with the photo or mark, a context line, the name, the statuses, the
  actions and the key facts, then tabs or sections in read mode;
- **form**: a title, then the sections, then a fixed action bar; from 1280 px a section index sits
  beside the form, and from 1700 px a help column too;
- **dashboard**: a title, then figures and short lists.

The content SHALL use the available width up to a maximum of 1680 px, with fixed side margins,
instead of a narrow centred column. Detail sections SHALL be laid out in a grid that adds columns
on wide screens. A form SHALL keep a readable width whatever the screen. Every page SHALL work from
360 px upwards without horizontal page scrolling (NFR-01). Long labels, in Valencian especially,
SHALL wrap instead of being truncated.

#### Scenario: Wide screen
- **WHEN** an arquebusier detail page is shown at 2560 px
- **THEN** the content spans up to 1680 px from the sidebar, and the personal data, license and course sections sit side by side

#### Scenario: Phone
- **WHEN** the same page is shown at 360 px
- **THEN** the sections are stacked in one column, every action can be reached and the page does not scroll horizontally

#### Scenario: Long labels
- **WHEN** a form is shown in Valencian at 360 px
- **THEN** every label and option is fully readable, wrapping onto more lines where needed

### Requirement: Detail pages in read mode
A record's detail page SHALL show its data read-only, grouped in sections, each with an "Edit"
action. "Edit" SHALL open the section's fields in a side panel, or in a bottom sheet on phones,
without leaving the page. Saving SHALL validate the same rules as the register form and show the
"Form fields and validation messages" errors. After saving, the panel SHALL close, the section SHALL
show the saved values, a short "saved" notice SHALL be announced without taking focus, and focus
SHALL return to the section's "Edit" action. Cancelling, Escape or closing the panel SHALL discard
the unsaved values. If someone else changed the record meanwhile, the panel SHALL stay open with the
translated reason and the current values. Empty values SHALL be shown as a translated "Not given".

#### Scenario: Edit a section
- **WHEN** a user opens "Edit" on an arquebusier's personal data, changes the phone and saves
- **THEN** the panel closes, the section shows the new phone, "Changes saved" is announced and focus is on that section's "Edit" action

#### Scenario: Cancel an edit
- **WHEN** a user changes a field in the panel and presses Escape
- **THEN** the panel closes, the section shows the previous values and nothing is sent to the server

#### Scenario: Invalid edit
- **WHEN** a user empties the last name in the panel and saves
- **THEN** the panel stays open with the error summary and the field error, and nothing is saved

#### Scenario: Concurrent change
- **WHEN** a user saves a section of a record that another user changed after the page loaded
- **THEN** the panel stays open, explains that someone else changed the record, and shows the current values to review

#### Scenario: Permission unchanged
- **WHEN** a FiringChief opens the detail page of an arquebusier of their comparsa
- **THEN** they see the same sections and edit actions as an Admin, except the Admin-only actions (such as transfer)

### Requirement: Action hierarchy
Each page SHALL have at most one primary action, shown at its natural width. On forms, it SHALL sit
at the end of an action bar that stays visible at the bottom of the screen. Secondary actions SHALL
use the secondary style. On detail pages, rarely used and destructive actions SHALL be grouped in a
"More actions" menu. Destructive items SHALL be separated from the others and shown in the
destructive colour with an icon. Only the confirming button of a destructive confirmation SHALL be
filled with the destructive colour. Two filled destructive buttons SHALL never appear side by side.

#### Scenario: Form action bar
- **WHEN** a user scrolls a long register form
- **THEN** the "Register arquebusier" button stays visible at the end of the bottom bar, with "Cancel" before it

#### Scenario: Destructive action in the menu
- **WHEN** an Admin opens "More actions" on a user's detail page
- **THEN** "Reset two-step verification" is a normal item and "Deactivate user" is set apart in the destructive colour, and choosing it asks for confirmation

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

### Requirement: Styled select
Selects SHALL keep the platform's native semantics and keyboard behaviour. Where the browser
supports styling its list, the list SHALL follow the menu style: surface, border, radius and
elevation from the tokens, a visible focus on the active option, and the chosen option marked with
an icon as well as weight. Where the browser does not support it, the native list SHALL be used. A
placeholder option such as "Choose a comparsa" SHALL NOT be offered as a choice in the list.

#### Scenario: Supporting browser
- **WHEN** a user opens the comparsa select in a browser that supports styled select lists
- **THEN** the list uses the PolvorApp menu style in the current theme and marks the chosen comparsa with a check icon

#### Scenario: Other browser
- **WHEN** a user opens the same select in a browser without that support
- **THEN** the native list opens and the field works the same

#### Scenario: Keyboard selection
- **WHEN** a keyboard user focuses the select, opens it with the keyboard, moves with the arrow keys and confirms
- **THEN** the option is chosen, the list closes and focus stays on the select

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

### Requirement: Charts
The composite layer SHALL offer line and bar charts (grouped, stacked and 100 % stacked) for
feature screens, built on the design tokens. Every chart SHALL:
- have a visible heading and a text summary, and SHALL offer the same data as a table with labelled
  rows and columns, so no figure is available only in the drawing (WCAG 1.1.1, 1.4.1);
- tell series apart by more than colour: a legend with the series names, and direct labels or
  distinct markers or patterns, and SHALL keep series colours from categorical chart tokens that
  reach 3:1 against the surface in both themes (WCAG 1.4.11);
- show a tooltip with the exact values on pointer hover and on keyboard focus of a data point, and
  SHALL let keyboard users move between data points;
- format numbers, percentages and years in the user's language;
- mark provisional values (for example with a hatched or outlined mark and the word "provisional"
  in the tooltip and the table);
- follow "Motion": at most a 200 ms fade on first render, none when the user asks for reduced
  motion, and no animation when the data changes;
- fit its container down to a 360 px screen without horizontal scrolling of the page.

#### Scenario: Chart with its table
- **WHEN** a screen reader user reaches a chart of active arquebusiers per edition
- **THEN** they hear its heading and summary, and can open the table with every value of the chart

#### Scenario: Keyboard tooltip
- **WHEN** a keyboard user focuses a chart and moves with the arrow keys to the 2026 point
- **THEN** a tooltip shows 2026 with its values, formatted in the user's language

#### Scenario: Series not by colour alone
- **WHEN** a stacked bar chart shows owned, rental, loan and none
- **THEN** a legend names each series, and each series is told apart by its pattern or label as well as its colour

#### Scenario: Chart contrast
- **WHEN** the chart series tokens are checked against the card surface in the light and dark themes
- **THEN** each reaches at least 3:1
