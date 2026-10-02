# Spec Delta

## MODIFIED Requirements

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
on the row SHALL open the record too. On screens narrower than the phone breakpoint, each row SHALL
be shown as a stacked item with the name, the identifier and the main status, and the page SHALL
NOT scroll horizontally. On wider screens, a table wider than its container SHALL scroll inside the
container. Row hover SHALL change the background at once, without a transition.

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
- **WHEN** a user clicks the comparsa cell of an arquebusier row
- **THEN** that arquebusier's detail page opens

#### Scenario: Keyboard opens the record
- **WHEN** a keyboard user tabs to a row and activates its name link
- **THEN** that arquebusier's detail page opens, and the row adds no extra tab stop

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
- **THEN** it shows the photo upload control empty, with a photo, disabled with its reason and with an error, in both themes and the three UI languages

## ADDED Requirements

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
- side panels, bottom sheets and the navigation drawer: 250 ms to appear.

Leaving SHALL be shorter than appearing. Route changes, sorting, filtering, paging, validation
messages, theme and language changes, and changes triggered by a keyboard shortcut SHALL NOT be
animated. Decorative motion (looping animations, staggered lists, hover scaling of rows) SHALL NOT
be used. When the user asks for reduced motion, movement and scaling SHALL be removed and only fades
of at most 100 ms SHALL remain. Animations SHALL never block input.

#### Scenario: Drawer on a phone
- **WHEN** a user opens the navigation drawer on a 360 px screen
- **THEN** it slides in within 250 ms and can be used during the animation

#### Scenario: Reduced motion
- **WHEN** a user whose system asks for reduced motion opens a side panel
- **THEN** the panel appears with a fade of at most 100 ms and without sliding

#### Scenario: Keyboard toggle
- **WHEN** a user collapses the sidebar with its keyboard shortcut
- **THEN** the sidebar collapses at once without animating its width

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
