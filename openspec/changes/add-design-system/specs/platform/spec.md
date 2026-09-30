# Spec Delta

## MODIFIED Requirements

### Requirement: Application shell
The UI SHALL render a shell on every route consisting of a sidebar (PolvorApp mark and name,
primary navigation with icons and optional counters, and the API version obtained from the system
information endpoint), a top bar (breadcrumbs, language switcher and theme switcher) and a main
content area. On screens narrower than the sidebar breakpoint the sidebar SHALL collapse into a
navigation drawer opened from the top bar. Unknown routes SHALL render a translated not-found page
inside the shell. The shell SHALL be usable from 360 px wide screens upwards (NFR-01),
keyboard-navigable with a visible focus indicator, SHALL mark the current navigation item and
SHALL have no automatically detectable WCAG 2.1 AA violations in either theme (NFR-07).

#### Scenario: Home route
- **WHEN** a user opens the application root
- **THEN** the shell renders with the PolvorApp mark, the navigation with the start page marked as current, the language and theme switchers and the API version

#### Scenario: API unavailable
- **WHEN** the system information endpoint fails
- **THEN** the shell still renders and the sidebar shows a translated "version unavailable" text

#### Scenario: Unknown route
- **WHEN** a user navigates to a route that does not exist
- **THEN** a translated not-found page with a link back to the start page is shown inside the shell

#### Scenario: Small screen navigation
- **WHEN** a user on a 360 px wide screen opens the navigation from the top bar
- **THEN** a drawer with the navigation opens, keeps keyboard focus inside and closes with Escape or after choosing a destination

#### Scenario: Accessibility check
- **WHEN** the shell and the not-found page are scanned by an automated accessibility checker in the light and dark themes
- **THEN** no WCAG 2.1 A or AA violations are reported

### Requirement: Security response headers
Every HTTP response served by the web entry point (UI and API) SHALL include
`X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, a `Content-Security-Policy`
that disallows framing (`frame-ancestors 'none'`), inline scripts and `eval`, and a `Permissions-Policy`
that allows the camera only for the application's own origin (NFR-01 photo capture). The API
SHALL only be reachable from the same origin as the UI; it SHALL NOT enable cross-origin requests.

#### Scenario: Headers on the UI
- **WHEN** a browser loads the UI through the web entry point
- **THEN** the response includes the four security headers

#### Scenario: Cross-origin request
- **WHEN** a request to the API arrives with an `Origin` header of another site
- **THEN** the response carries no `Access-Control-Allow-Origin` header

#### Scenario: Scripts are strict, styles may be inline
- **WHEN** the Content-Security-Policy of the UI is inspected
- **THEN** `script-src` is exactly `'self'` and no directive allows `unsafe-eval`
- **AND** `style-src` may allow `'unsafe-inline'`, because UI primitives inject scroll-lock styles at runtime (design-system change, D10)
