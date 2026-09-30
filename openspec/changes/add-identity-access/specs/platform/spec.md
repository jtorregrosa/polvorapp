# Spec Delta

## ADDED Requirements

### Requirement: Authenticated API by default
Every API endpoint SHALL require a signed-in user who has completed two-factor authentication,
unless it is explicitly declared anonymous. The anonymous endpoints SHALL be limited to: health,
system information, the API description (Development only), the anti-forgery token endpoint and the
sign-in, invitation, enrolment and password-reset steps. Anonymous endpoints SHALL NOT return
personal data other than, for a valid invitation link, the invitee's own name and email. Requests
without a valid session SHALL receive `401 Unauthorized` and requests from a user whose role does not
allow the endpoint SHALL receive `403 Forbidden`, both as problem-details documents.

#### Scenario: Protected endpoint without session
- **WHEN** a client without a session calls `GET /api/users`
- **THEN** the API responds `401 Unauthorized` with a problem-details body

#### Scenario: Anonymous endpoints stay open
- **WHEN** a client without a session calls `GET /api/health/ready` or `GET /api/system/info`
- **THEN** the API responds as before, without requiring a session

#### Scenario: New endpoint without declaration
- **WHEN** a developer adds an endpoint without declaring it anonymous
- **THEN** calling it without a session responds `401 Unauthorized`

#### Scenario: Unknown API route
- **WHEN** a client calls an API path that is not defined, such as `GET /api/arquebusiers`
- **THEN** the API responds `404 Not Found` or `401 Unauthorized` with a problem-details body and reveals no data

### Requirement: Transactional email
The platform SHALL send transactional emails (NFR-11) through an SMTP server configured by
environment variables (host, port, sender address and optional credentials, plus the public base
URL used in links). Emails SHALL be sent in the recipient's language with a plain-text part, SHALL
contain no personal data beyond what the message needs, and their contents, links and tokens SHALL
NOT be written to logs. A missing or invalid email setting SHALL stop the API from starting, naming
the setting but not its value. In the local environment emails SHALL be delivered to the mail catcher.

#### Scenario: Local invitation email
- **WHEN** an invitation is sent in the local environment
- **THEN** the message appears in the mail catcher with a link to the local UI origin

#### Scenario: Missing SMTP host
- **WHEN** the API starts without the SMTP host setting
- **THEN** the process exits with a non-zero code and logs which setting is missing

#### Scenario: SMTP server unavailable
- **WHEN** the SMTP server rejects or cannot be reached while sending an invitation
- **THEN** the operation reports a translated "email could not be sent" error, the failure is logged without the message content, and the invitation can be resent later

## MODIFIED Requirements

### Requirement: Application shell
The UI SHALL render a shell on every signed-in route consisting of a sidebar (PolvorApp mark and
name, primary navigation with icons and optional counters showing only the destinations allowed for
the user's role, and the API version obtained from the system information endpoint), a top bar
(breadcrumbs, language switcher, theme switcher and a user menu with the user's name and role, a
link to the account page and a sign-out action) and a main content area. Signed-out pages (sign-in,
two-factor, invitation, enrolment, password reset) SHALL render in a public layout with the
PolvorApp mark, the language and theme switchers and the API version, without navigation. On
screens narrower than the sidebar breakpoint the sidebar SHALL collapse into a navigation drawer
opened from the top bar. Unknown routes SHALL render a translated not-found page inside the shell,
and routes not allowed for the user's role SHALL render a translated "not allowed" page inside the
shell. The shell and the public layout SHALL be usable from 360 px wide screens upwards (NFR-01),
keyboard-navigable with a visible focus indicator, SHALL mark the current navigation item and SHALL
have no automatically detectable WCAG 2.1 AA violations in either theme (NFR-07).

#### Scenario: Home route
- **WHEN** a signed-in user opens the application root
- **THEN** the shell renders with the PolvorApp mark, the navigation with the start page marked as current, the language and theme switchers, the user menu and the API version

#### Scenario: Signed-out visitor
- **WHEN** a visitor without a session opens any application route
- **THEN** the sign-in page is shown in the public layout and, after signing in, the visitor is taken to the route they requested

#### Scenario: Navigation follows the role
- **WHEN** a FiringChief is signed in
- **THEN** the navigation does not show the users entry, which an Admin sees

#### Scenario: API unavailable
- **WHEN** the system information endpoint fails
- **THEN** the shell or public layout still renders and shows a translated "version unavailable" text

#### Scenario: Unknown route
- **WHEN** a signed-in user navigates to a route that does not exist
- **THEN** a translated not-found page with a link back to the start page is shown inside the shell

#### Scenario: Small screen navigation
- **WHEN** a user on a 360 px wide screen opens the navigation from the top bar
- **THEN** a drawer with the navigation opens, keeps keyboard focus inside and closes with Escape or after choosing a destination

#### Scenario: Accessibility check
- **WHEN** the shell, the not-found page and the sign-in page are scanned by an automated accessibility checker in the light and dark themes
- **THEN** no WCAG 2.1 A or AA violations are reported

### Requirement: Switch UI language (UC-27)
The UI SHALL be available in `es-ES`, `ca-ES-valencia` and `en`, and the language switcher SHALL
change every visible text at runtime without reloading the page. On first visit the UI SHALL use
the browser's preferred language when it is supported, otherwise `es-ES`. The chosen language SHALL
be remembered in the browser across visits, SHALL be sent to the API as `Accept-Language`, and the
document `lang` attribute SHALL always match the active language. When a user signs in, the UI SHALL
switch to that user's preferred `locale`; when a signed-in user changes the language, it SHALL also
be saved as their preferred `locale`, which is used for the emails they receive. Dates and numbers
SHALL be formatted for the active language. Each language SHALL be listed in the switcher by its own
name.

#### Scenario: Switching to Valencian
- **WHEN** a user selects "Valencià" in the language switcher
- **THEN** all shell texts change to Valencian without a page reload
- **AND** the document `lang` attribute becomes `ca-ES-valencia`

#### Scenario: Choice is remembered
- **WHEN** a user who selected English reloads the page
- **THEN** the UI is shown in English

#### Scenario: Unsupported browser language
- **WHEN** a first-time user's browser prefers only `fr-FR`
- **THEN** the UI is shown in Spanish (`es-ES`)

#### Scenario: Language is sent to the API
- **WHEN** the UI calls the API while Valencian is active
- **THEN** the request carries `Accept-Language: ca-ES-valencia`

#### Scenario: Preferred language applied at sign-in
- **WHEN** a user whose `locale` is `ca-ES-valencia` signs in on a browser showing Spanish
- **THEN** the UI switches to Valencian after sign-in

#### Scenario: Signed-in change is saved
- **WHEN** a signed-in user switches the language to English
- **THEN** their `locale` becomes `en` and later emails to them are in English

### Requirement: Local environment with one command
The repository SHALL start a complete local environment — API, web entry point, database, object
storage and mail catcher — with a single container-orchestration command using only the values from
`.env.example`, and the UI SHALL then be served on one origin that proxies `/api` to the API
(ADR-0006, ADR-0011). Database migrations SHALL be applied by an explicit host command, never
implicitly when the web API starts, and the local environment SHALL run it before the API starts.

#### Scenario: Fresh clone
- **WHEN** a developer clones the repository, copies `.env.example` to `.env` and starts the environment
- **THEN** the sign-in page loads in the browser and shows the API version
- **AND** the readiness endpoint reports `Healthy`

#### Scenario: Synthetic users after seeding
- **WHEN** the developer runs the seed command in the local environment
- **THEN** they can sign in as the synthetic Admin using the credentials and authenticator key configured in `.env`

## REMOVED Requirements

### Requirement: Anonymous surface is limited until identity exists
**Reason**: Identity now exists; the API is authenticated by default.
**Migration**: Replaced by "Authenticated API by default"; health and system information stay anonymous.
