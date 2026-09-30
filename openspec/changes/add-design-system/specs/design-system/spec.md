# Spec Delta

## Purpose

Gives every PolvorApp screen one coherent, accessible and themeable look through closed design
tokens, a layer of PolvorApp composites, shared status semantics, a component catalogue and the
guardrails that stop feature code from bypassing them (ADR-0009, ADR-0012).

## ADDED Requirements

### Requirement: PolvorApp visual identity
The UI SHALL present PolvorApp's own identity — its name, its own mark and its own palette — in the
application, the browser tab and the installed app. The repository SHALL NOT contain logos or
brand assets of the Federation or any other organisation (ADR-0012).

#### Scenario: Installed application
- **WHEN** a user installs the application on a phone or desktop
- **THEN** it is shown with the name "PolvorApp", the PolvorApp mark and the theme colour defined by the design tokens

#### Scenario: Browser tab
- **WHEN** a user opens any page
- **THEN** the tab shows the PolvorApp favicon and a title ending in "PolvorApp"

### Requirement: Design tokens with light and dark themes
All colours, font families, font sizes, spacing, radii, shadows and breakpoints used by the UI
SHALL come from one set of design tokens defined for a light and a dark theme. Colours SHALL be
semantic (`background`, `foreground`, `primary`, `muted`, `border`, `success`, `warning`,
`destructive`, `info`, …). In both themes, every text token SHALL reach a contrast of at least
4.5:1 against the surfaces it is used on, and interactive boundaries and the focus indicator at
least 3:1 (WCAG 2.1 AA, NFR-07).

#### Scenario: Contrast verified
- **WHEN** the token contrast checks run
- **THEN** every declared text/surface and indicator/surface pair meets its minimum ratio in both themes

#### Scenario: Insufficient contrast
- **WHEN** a token is changed so that a declared pair falls below its minimum ratio
- **THEN** the contrast check fails naming the theme, the pair and the measured ratio

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
`LicenseStatus` (`VALID` → success, `EXPIRED` → destructive, `PENDING` → info) plus the derived
"expiring soon" state of a `VALID` license (→ warning);
`ArquebusierStatus` (`ACTIVE` → success, `RESERVE` → muted); `ComparsaOrder` status (`DRAFT` → muted,
`SUBMITTED` → info, `RETURNED` → warning, `VALIDATED` → success); `FestivalEdition` status (`DRAFT`,
`CLOSED` → muted, `ORDERS_OPEN` → success, `CORRECTIONS_OPEN` → warning, `LOCKED` → info) and
compliance warnings (BR-04 → warning). Compliance warnings SHALL never use the destructive colour,
because they do not block (BR-04 is a warning).

#### Scenario: Expired license
- **WHEN** a license with status `EXPIRED` is rendered in Valencian
- **THEN** the badge shows the Valencian label for "expired", the destructive colour and an icon

#### Scenario: Compliance warning is not an error
- **WHEN** a missing-course warning (BR-04) is rendered
- **THEN** it uses the warning colour and icon, not the destructive ones

#### Scenario: Unknown status value
- **WHEN** a status value outside the mapping is rendered
- **THEN** a neutral badge with the raw code is shown and the case is reported in development builds

### Requirement: Accessible composites
Every composite SHALL be operable with the keyboard alone, expose correct names, roles and states
to assistive technology, keep a visible focus indicator and have no automatically detectable WCAG
2.1 A/AA violations in either theme. Dialogs SHALL move focus into the dialog, keep it there, close
with Escape and return focus to the element that opened them.

#### Scenario: Dialog focus
- **WHEN** a keyboard user opens a confirmation dialog, presses Escape
- **THEN** the dialog closes and focus returns to the button that opened it

#### Scenario: Automated checks
- **WHEN** the automated accessibility checks run over every composite in both themes
- **THEN** no WCAG 2.1 A or AA violations are reported

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
Form fields SHALL show a visible label, a required marker where applicable, optional help text
and, on invalid input, a translated error message programmatically associated with the field.
When a submitted form has errors, focus SHALL move to the first invalid field.

#### Scenario: Invalid field
- **WHEN** a user submits a form with an invalid required field
- **THEN** the field is marked invalid, its translated error is announced with it and focus moves to it

### Requirement: Data tables
Tabular lists SHALL support sorting by column, pagination with a translated summary, a loading
state and an empty state with a translated message. On screens narrower than the table, the table
SHALL scroll horizontally inside its container without making the page scroll horizontally.

#### Scenario: Empty list
- **WHEN** a table has no rows
- **THEN** a translated empty-state message is shown instead of an empty grid

#### Scenario: Narrow screen
- **WHEN** a wide table is shown on a 360 px screen
- **THEN** the table scrolls horizontally inside its container and the page does not

### Requirement: Component catalogue
Every composite SHALL have an entry in the component catalogue showing its states in the three UI
languages and both themes. The build pipeline SHALL fail when a composite has no catalogue entry or
when any catalogue entry has an accessibility violation.

#### Scenario: Composite without catalogue entry
- **WHEN** a new composite is added without a catalogue entry
- **THEN** the pipeline fails naming the composite

### Requirement: Design guide
The repository SHALL contain a design guide describing the principles, tokens, status semantics,
page templates (list, detail, form, dashboard), validation and error patterns, destructive
actions, empty and loading states, responsive rules and microcopy tone in the three languages, and
it SHALL be referenced from the project context used for new changes.

#### Scenario: New change context
- **WHEN** a contributor or agent starts a new change
- **THEN** the project context points to the design guide as the UI reference
