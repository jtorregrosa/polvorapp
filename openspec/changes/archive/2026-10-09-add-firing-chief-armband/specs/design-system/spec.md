# Spec Delta

## MODIFIED Requirements

### Requirement: PolvorApp visual identity
The UI SHALL present PolvorApp's own identity in the application, the browser tab and the installed
app: its name, its own mark, its own palette and its own typefaces. Titles and key figures SHALL use
a display typeface, the interface a text typeface and identifiers (nationalId, federationId, guide
numbers) a monospaced typeface. All of them SHALL be served by the application itself under a
licence that allows redistribution. The navigation sidebar SHALL keep its dark "night" surface in
both themes, with the brand accent as the only accent colour. The FiringChief armband (platform
spec) is an insignia, not an accent: its yellow and its text colour SHALL be design tokens of their
own, used for the armband only and never for actions, links, focus, selection, the current item or
statuses, and their contrast SHALL be verified on the night surface in both themes. The repository
SHALL NOT contain logos or brand assets of the Federation or any other organisation (ADR-0013).

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

#### Scenario: Armband colours in both themes
- **WHEN** the contrast of the design tokens is checked in the light and dark themes
- **THEN** the armband text meets 4.5:1 against the armband yellow, and the yellow meets 3:1 against the night sidebar

#### Scenario: Armband yellow is not reused
- **WHEN** any screen shows a warning, a link, a focused control or the current navigation item
- **THEN** none of them uses the armband tokens
