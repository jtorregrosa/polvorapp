# Spec Delta

## ADDED Requirements

### Requirement: FiringChief armband
For a signed-in FiringChief, the sidebar of the application shell SHALL show an armband that echoes
the yellow armband FiringChiefs wear at the festival. It SHALL be a horizontal yellow band across the
full width of the sidebar, placed below the navigation and above the API version, and it SHALL stay
visible while the navigation scrolls. It SHALL show the translated name of the FiringChief role in
capitals, in the current UI language ("Jefe de disparo" in es-ES, "Cap de disparada" in
ca-ES-valencia, "Firing chief" in en), and SHALL change at once when the user switches language. A
label that does not fit SHALL wrap rather than be clipped.

The armband SHALL be plain text: not a link, not a button, with no icon and no tooltip. Admins SHALL
see no armband. It does not depend on comparsa assignments: a FiringChief without active
assignments SHALL still see it. It SHALL NOT be shown in the public (signed-out) layout or in the
phone's bottom navigation bar.

In the icon rail, the armband SHALL become a yellow stripe across the rail without text, hidden from
assistive technology, and it SHALL keep its place, so nothing below or above it moves when the
sidebar collapses or expands. On phones, the navigation drawer SHALL show the armband as in the
expanded sidebar.

#### Scenario: Armband for a FiringChief
- **WHEN** a FiringChief is signed in with the UI in es-ES and the sidebar expanded
- **THEN** the bottom of the sidebar shows a yellow band across its full width reading "JEFE DE DISPARO", above the API version

#### Scenario: Armband follows the language
- **WHEN** a signed-in FiringChief switches the UI language to ca-ES-valencia
- **THEN** the armband reads "CAP DE DISPARADA" at once

#### Scenario: No armband for Admins
- **WHEN** an Admin is signed in
- **THEN** the sidebar shows no armband

#### Scenario: FiringChief without comparsas
- **WHEN** a FiringChief with no active comparsa assignment is signed in
- **THEN** the sidebar shows no comparsa card but still shows the armband

#### Scenario: Armband stays in view
- **WHEN** a FiringChief scrolls the navigation on a screen too short to show all of it
- **THEN** the armband stays visible at the bottom of the sidebar

#### Scenario: Armband is not interactive
- **WHEN** a FiringChief moves through the sidebar with the Tab key
- **THEN** focus never lands on the armband, and a screen reader reads its text as plain text

#### Scenario: Armband in the icon rail
- **WHEN** a FiringChief on a 1440 px screen collapses the sidebar
- **THEN** the armband becomes a yellow stripe without text in the same place, assistive technology does not announce it, and no tooltip is shown for it

#### Scenario: Armband in the drawer
- **WHEN** a FiringChief on a 360 px screen opens the navigation drawer
- **THEN** the drawer shows the armband with its text at the bottom, and the bottom navigation bar shows no armband

#### Scenario: Armband with enlarged text
- **WHEN** a FiringChief with the UI in ca-ES-valencia views the sidebar with text spacing increased to the WCAG 1.4.12 values
- **THEN** the whole armband label stays visible, wrapping if needed, without being clipped

#### Scenario: Armband accessibility check
- **WHEN** the shell of a signed-in FiringChief is scanned by an automated accessibility checker with the sidebar expanded and collapsed, in the light and dark themes
- **THEN** no WCAG 2.2 A or AA violations are reported
