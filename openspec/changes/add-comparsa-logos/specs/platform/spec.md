# Spec Delta

## MODIFIED Requirements

### Requirement: Application shell
The UI SHALL render a shell on every signed-in route consisting of:
- a sidebar with the dark "night" surface in both themes, holding the PolvorApp mark and name, the
  primary navigation (icons and optional counters, showing only the destinations allowed for the
  user's role) and the API version obtained from the system information endpoint. For a
  FiringChief, the sidebar SHALL also show, between the mark and the navigation, one card for each
  active comparsa assigned to them, sorted by name. Each card SHALL show the comparsa's logo, or
  the placeholder when it has none, and its name, and SHALL link to the comparsa's detail page.
  Admins and FiringChiefs without active assignments SHALL see no card. When the cards cannot be
  loaded, the sidebar SHALL still show the mark and the navigation;
- a top bar with the breadcrumbs and a user menu. The user menu SHALL hold the user's name and role,
  the language switcher, the theme switcher, a link to the account page and a sign-out action;
- a main content area that uses the width beside the sidebar up to 1680 px. The top bar's
  breadcrumbs and user menu SHALL be aligned with the edges of that content.

Signed-out pages (sign-in, two-factor, invitation, enrolment, password reset) SHALL render in a
public layout with the PolvorApp mark, the language and theme switchers and the API version,
without navigation. On screens narrower than the sidebar breakpoint, the sidebar SHALL collapse into
a navigation drawer opened from the top bar, appearing within 250 ms. Unknown routes SHALL render a
translated not-found page inside the shell. Routes not allowed for the user's role SHALL render a
translated "not allowed" page inside the shell.

The shell and the public layout SHALL be usable from 360 px wide screens upwards (NFR-01) and
keyboard-navigable with a visible focus indicator. They SHALL mark the current navigation item by
weight and an indicator bar, not by colour alone. They SHALL have no automatically detectable WCAG
2.2 AA violations in either theme (NFR-07).

#### Scenario: Home route
- **WHEN** a signed-in user opens the application root
- **THEN** the shell renders with the PolvorApp mark, the navigation with the start page marked as current, the breadcrumbs, the user menu and the API version

#### Scenario: Preferences in the user menu
- **WHEN** a signed-in user opens the user menu
- **THEN** it shows their name and role, the language and theme switchers, the account link and the sign-out action, and choosing a language changes the UI at once

#### Scenario: Signed-out visitor
- **WHEN** a visitor without a session opens any application route
- **THEN** the sign-in page is shown in the public layout, with the language and theme switchers visible, and after signing in the visitor is taken to the route they requested

#### Scenario: Navigation follows the role
- **WHEN** a FiringChief is signed in
- **THEN** the navigation does not show the users entry, which an Admin sees

#### Scenario: FiringChief's comparsas in the sidebar
- **WHEN** a FiringChief assigned to two active comparsas, one with a logo and one without, is signed in
- **THEN** the sidebar shows two cards sorted by name, one with the logo and one with the placeholder, each with the comparsa name and a link to its detail page

#### Scenario: Inactive comparsa not in the sidebar
- **WHEN** a FiringChief assigned to one active and one inactive comparsa is signed in
- **THEN** the sidebar shows a card only for the active comparsa

#### Scenario: No comparsa cards for Admins
- **WHEN** an Admin is signed in
- **THEN** the sidebar shows no comparsa card

#### Scenario: Comparsa cards on a small screen
- **WHEN** a FiringChief on a 360 px wide screen opens the navigation drawer
- **THEN** the drawer shows their comparsa cards above the navigation, and choosing a card opens the comparsa and closes the drawer

#### Scenario: API unavailable
- **WHEN** the system information endpoint fails
- **THEN** the shell or public layout still renders and shows a translated "version unavailable" text

#### Scenario: Unknown route
- **WHEN** a signed-in user navigates to a route that does not exist
- **THEN** a translated not-found page with a link back to the start page is shown inside the shell

#### Scenario: Small screen navigation
- **WHEN** a user on a 360 px wide screen opens the navigation from the top bar
- **THEN** a drawer with the navigation opens within 250 ms, keeps keyboard focus inside, and closes with Escape or after choosing a destination, returning focus to the menu button when closed with Escape

#### Scenario: Wide screen
- **WHEN** a signed-in user opens the arquebusiers list in a 2560 px window
- **THEN** the content spans from the sidebar up to 1680 px instead of a narrow centred column, and the breadcrumbs and the user menu are aligned with the left and right edges of that content

#### Scenario: Accessibility check
- **WHEN** the shell, the not-found page and the sign-in page are scanned by an automated accessibility checker in the light and dark themes
- **THEN** no WCAG 2.2 A or AA violations are reported
