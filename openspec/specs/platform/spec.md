# platform Specification

## Purpose

Provides the runtime foundation every PolvorApp capability relies on: health and version
endpoints, safe error, logging, configuration and header behaviour, request localisation, the
guarded synthetic seed, the UI shell with language switching (UC-27), the generated API contract,
module boundaries and the quality gates that protect them.

## Requirements

### Requirement: Health endpoints
The API SHALL expose an anonymous liveness endpoint at `GET /api/health/live` and an anonymous
readiness endpoint at `GET /api/health/ready`. Liveness SHALL report healthy whenever the process
can serve requests, without checking dependencies. Readiness SHALL report healthy only when the
database is reachable and the object storage bucket is reachable. Neither response SHALL contain
connection strings, host names, bucket names, exception messages or personal data.

#### Scenario: API and database are up
- **WHEN** a client calls `GET /api/health/ready` and the database accepts connections and the storage bucket is reachable
- **THEN** the API responds `200 OK` with status `Healthy`

#### Scenario: Database is unreachable
- **WHEN** a client calls `GET /api/health/ready` and the database does not accept connections
- **THEN** the API responds `503 Service Unavailable` with status `Unhealthy`
- **AND** the body names the failing check (`database`) without exception details or connection information

#### Scenario: Storage is unreachable
- **WHEN** a client calls `GET /api/health/ready` and the storage bucket cannot be reached
- **THEN** the API responds `503 Service Unavailable` with status `Unhealthy`
- **AND** the body names the failing check (`storage`) without exception details, host or bucket names

#### Scenario: Liveness ignores dependencies
- **WHEN** a client calls `GET /api/health/live` while the database or the storage is unreachable
- **THEN** the API responds `200 OK`

### Requirement: System information endpoint
The API SHALL expose an anonymous `GET /api/system/info` endpoint returning the application
version and the build commit identifier. It SHALL NOT return environment variables, configuration
values, host details or personal data.

#### Scenario: Version is returned
- **WHEN** a client calls `GET /api/system/info`
- **THEN** the API responds `200 OK` with `version` and `commit` fields

#### Scenario: No sensitive data is disclosed
- **WHEN** a client calls `GET /api/system/info`
- **THEN** the response contains only the documented fields

### Requirement: Problem details error responses
The API SHALL return every error as an RFC 9457 problem-details document with `type`, `title`,
`status` and a `traceId`. Outside the Development environment, error responses SHALL NOT include
exception messages, stack traces or internal type names.

#### Scenario: Unhandled exception in Production
- **WHEN** a request fails with an unhandled exception in the Production environment
- **THEN** the API responds `500 Internal Server Error` with a problem-details body containing a `traceId`
- **AND** the body contains no exception message or stack trace

#### Scenario: Unhandled exception in Development
- **WHEN** a request fails with an unhandled exception in the Development environment
- **THEN** the problem-details body includes the exception details to aid debugging

### Requirement: Structured logging without personal data
The API SHALL write structured logs in which every request log entry carries a correlation
identifier that is also returned to the client (as the problem-details `traceId` on errors and as
a response header). Logs SHALL NOT contain request bodies, query-string values, cookies,
authorization headers or other personal data (NFR-12).

#### Scenario: Correlation identifier
- **WHEN** a client calls any API endpoint
- **THEN** the response carries a correlation identifier header
- **AND** the log entries for that request contain the same identifier

#### Scenario: Query-string values are not logged
- **WHEN** a client calls an API endpoint with a query string
- **THEN** the request log entry records the path but not the query-string values

### Requirement: Configuration from environment with fail-fast validation
The API SHALL read all environment-specific configuration and secrets from environment variables
(or files referenced by them), never from values committed to the repository. At startup the API
SHALL validate required settings and SHALL refuse to start, naming the missing setting but not its
value, when one is missing or invalid. The repository SHALL contain an `.env.example` with
placeholder values only (NFR-14).

#### Scenario: Missing database connection setting
- **WHEN** the API starts without the database connection setting
- **THEN** the process exits with a non-zero code and logs which setting is missing

#### Scenario: Example environment file
- **WHEN** a developer copies `.env.example` to `.env`
- **THEN** every variable required by the local environment is present with a non-secret placeholder value

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

### Requirement: Request culture resolution
The API SHALL resolve the request culture from the `Accept-Language` header among `es-ES`,
`ca-ES-valencia` and `en`, falling back to `es-ES` when none matches, and SHALL use it for
user-facing texts it returns (such as problem-details titles). Stored values and codes SHALL NOT
depend on the culture (ADR-0007).

#### Scenario: Valencian request
- **WHEN** a client calls an endpoint that returns a user-facing text with `Accept-Language: ca-ES-valencia`
- **THEN** the text is returned in Valencian

#### Scenario: Unsupported language
- **WHEN** a client sends `Accept-Language: de-DE`
- **THEN** user-facing texts are returned in Spanish (`es-ES`)

#### Scenario: Generic Catalan request
- **WHEN** a client sends `Accept-Language: ca` or `ca-ES`
- **THEN** user-facing texts are returned in Valencian, the only Catalan variant offered

### Requirement: Guarded synthetic seed
The platform SHALL provide a single command that populates a database with synthetic,
deterministic data produced by the registered seeders of each module (SEC-11). The command SHALL
run only in the Development, Staging (synthetic data only, NFR-13) and Testing environments and
SHALL refuse any other environment, including Production and unknown names. Seeders SHALL NOT read
from files outside the repository's synthetic-data sources.

The command SHALL offer two datasets, chosen by a seed setting:
- *scenarios*, the default: the fixed cases each module's seed requirement lists, at their current
  size;
- *full*: the scenarios plus a realistic population at the festival's scale.

An unknown dataset name SHALL be refused before anything is written. Running the full dataset on a
database seeded with the scenarios SHALL add the population and leave the scenario rows as they
are.

#### Scenario: Seeding in Development
- **WHEN** the seed command runs in the Development environment
- **THEN** every registered seeder runs and the command exits successfully

#### Scenario: Seeding in Production is blocked
- **WHEN** the seed command runs with the environment set to Production
- **THEN** it exits with a non-zero code without writing to the database and logs the refusal

#### Scenario: Unknown environment is blocked
- **WHEN** the seed command runs with the environment set to a name outside the allowed list, such as `Prod`
- **THEN** it exits with a non-zero code without writing to the database and logs the refusal

#### Scenario: Deterministic output
- **WHEN** the seed command runs twice against two empty databases with the same dataset
- **THEN** both databases contain identical data

#### Scenario: Scenarios dataset by default
- **WHEN** the seed command runs without a dataset setting
- **THEN** only the scenario data is created

#### Scenario: Full dataset
- **WHEN** the seed command runs with the full dataset
- **THEN** the scenario data and the realistic population are created

#### Scenario: Full dataset after the scenarios
- **WHEN** the seed command runs with the full dataset on a database already seeded with the scenarios
- **THEN** the population is added and the scenario rows are unchanged

#### Scenario: Unknown dataset is refused
- **WHEN** the seed command runs with the dataset set to `huge`
- **THEN** it exits with a non-zero code without writing to the database and logs the refusal

### Requirement: Application shell
The UI SHALL render a shell on every signed-in route consisting of:
- a sidebar with the dark "night" surface in both themes, holding the PolvorApp mark and name, the
  primary navigation (icons and optional counters, showing only the destinations allowed for the
  user's role, in labelled sections) and the API version obtained from the system information endpoint. For a
  FiringChief, the sidebar SHALL also show, between the mark and the navigation, one card for each
  active comparsa assigned to them, sorted by name. Each card SHALL show the comparsa's logo, or
  the placeholder when it has none, and its name, and SHALL link to the comparsa's detail page.
  Admins and FiringChiefs without active assignments SHALL see no card. When the cards cannot be
  loaded, the sidebar SHALL still show the mark and the navigation;
- a top bar with the breadcrumbs and a user menu. The user menu SHALL hold the user's name and role,
  the language switcher, the theme switcher, a link to the account page and a sign-out action;
- a main content area that uses the width beside the sidebar up to 1680 px. The top bar's
  breadcrumbs and user menu SHALL be aligned with the edges of that content;
- for a FiringChief on a phone (below 768 px), a bottom navigation bar with Home, Arquebusiers,
  Orders and Distribution, marking the current one; the drawer keeps the full navigation. A form's
  own bottom action bar takes its place.

The primary navigation SHALL be grouped in sections, in this order, each showing only the entries
allowed for the user's role and hidden when it has none:
- no label: Home;
- "Registry": Arquebusiers, Comparsas, Statistics;
- "Festival": Editions, Orders, Distribution;
- "Administration" (Admins only): Weapon models, Users, Audit log, Privacy.

Each section SHALL be a labelled group for assistive technology. The current entry SHALL be the one
that owns the page: Orders on an edition's orders and exports pages, Distribution on an edition's
distribution page, and Editions on the other edition pages.

On screens from the sidebar breakpoint upwards, the sidebar SHALL collapse to an icon rail instead
of disappearing, from a button in the top bar or with its keyboard shortcut. In the rail:
- each entry SHALL show its icon only, keep its translated name as its accessible name, and show
  the name in a tooltip on pointer hover and keyboard focus;
- a counter SHALL become a dot, with the count kept in the entry's accessible name;
- section labels SHALL be replaced by separators;
- the FiringChief's comparsa cards SHALL show only the logo, or the placeholder, with the comparsa
  name as accessible name and tooltip;
- the API version SHALL be hidden.

Every entry SHALL stay reachable with the keyboard in both states. The expanded or collapsed state
SHALL be remembered on the device and restored on the next visit; it SHALL default to expanded.

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

#### Scenario: Navigation sections
- **WHEN** an Admin is signed in with the sidebar expanded
- **THEN** the navigation shows Home, then the "Registry", "Festival" and "Administration" sections with their entries, each section announced as a named group

#### Scenario: No administration section for FiringChiefs
- **WHEN** a FiringChief is signed in
- **THEN** the "Administration" section and its label are not shown

#### Scenario: Edition sub-page marks its entry
- **WHEN** a user opens the distribution page of the current edition at `/editions/{id}/distribution`
- **THEN** Distribution is marked as the current entry, and Editions is not

#### Scenario: Icon rail
- **WHEN** a user on a 1440 px screen collapses the sidebar and moves focus to the Orders entry
- **THEN** the sidebar shows only icons, the Orders entry is named "Orders" for assistive technology, and a tooltip shows its name

#### Scenario: Counter in the rail
- **WHEN** the sidebar is collapsed and 5 arquebusiers have warnings
- **THEN** the Arquebusiers entry shows a dot and its accessible name still says 5 with warnings

#### Scenario: Remembered state
- **WHEN** a user collapses the sidebar and reloads the page
- **THEN** the sidebar is still collapsed

#### Scenario: Phones keep the drawer
- **WHEN** a user on a 360 px screen opens the navigation from the top bar
- **THEN** the full navigation opens in a drawer with section labels, never as an icon rail

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

### Requirement: Translation completeness
Every user-facing UI text SHALL come from translation resources; the build pipeline SHALL fail
when a text is hard-coded in UI code or when any key present in one locale is missing or empty in
another (NFR-02, ADR-0007).

#### Scenario: Missing key in one locale
- **WHEN** a key exists in the `es-ES` resources but not in `ca-ES-valencia`
- **THEN** the translation check fails naming the locale and the key

#### Scenario: Hard-coded text
- **WHEN** UI code renders a literal user-facing string instead of a translation
- **THEN** the lint check fails

### Requirement: Generated API contract
The API SHALL publish an OpenAPI description of all its endpoints, and a copy SHALL be committed
to the repository. The UI SHALL call the API only through a client generated from that
description. The pipeline SHALL fail when the committed description differs from the one the API
produces.

#### Scenario: Contract drift
- **WHEN** an endpoint changes but the committed OpenAPI description is not regenerated
- **THEN** the contract check fails

### Requirement: Module boundaries
Backend code SHALL be organised in modules, one per capability (ADR-0001). A module SHALL NOT
depend on another module's internal types or persistence; it may only use another module's public
contract. The shared kernel SHALL NOT depend on any module or on the API host. Violations SHALL fail
the test suite.

#### Scenario: Cross-module internal reference
- **WHEN** code in one module references a non-contract type of another module
- **THEN** the architecture tests fail naming both modules

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

### Requirement: Installable application
The UI SHALL provide a web app manifest and a service worker so that it can be installed on
phones and desktops (NFR-03). The service worker SHALL cache only the application's static assets
and SHALL NOT cache any `/api` response. The installed application SHALL open the distribution
capture screen (UC-21) without connectivity from those cached assets. Its data comes from the
device's own store (see the distribution spec, "Data kept on the device"), never from a cached
`/api` response. Every other screen SHALL keep needing the network, and SHALL say so when it
cannot reach it.

#### Scenario: API responses are never cached
- **WHEN** the installed application calls an `/api` endpoint
- **THEN** the response is fetched from the network, not from the service-worker cache

#### Scenario: Capture screen opens offline
- **WHEN** an Admin whose device holds a capture package opens the installed application on the capture route without connectivity
- **THEN** the capture screen is shown with the package's holders

#### Scenario: Other screens need the network
- **WHEN** a user opens the arquebusiers list without connectivity
- **THEN** the page says that it cannot reach the server, and shows no stale data

### Requirement: Continuous integration quality gates
Every pull request and every push to `main` SHALL run: backend build, format check and tests;
UI lint, type check, translation check, unit tests and build; the contract check; end-to-end tests
against the containerised environment; container image builds; secret scanning; and dependency
vulnerability checks. Any failure SHALL mark the run as failed. Tests SHALL use synthetic data only.

#### Scenario: Committed secret
- **WHEN** a pull request adds a file containing a credential-like secret
- **THEN** the secret-scanning job fails

#### Scenario: Vulnerable dependency
- **WHEN** a pull request introduces a dependency with a known high or critical vulnerability
- **THEN** the dependency check fails

### Requirement: Open-source licensing
The repository SHALL contain an MIT `LICENSE` file (ADR-0010) and a register of third-party
dependency licenses; a dependency whose license is not MIT-compatible SHALL only be added after its
terms are recorded as acceptable for the Federation's use in that register.

#### Scenario: License file present
- **WHEN** anyone inspects the repository root
- **THEN** a `LICENSE` file with the MIT license text is present

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

### Requirement: Private object storage
The API SHALL keep files in an S3-compatible object storage (ADR-0005): MinIO in the local
environment, and any S3-compatible provider in an EU region elsewhere. Its endpoint, bucket and
credentials SHALL come from the environment and SHALL be validated at startup. A missing or invalid
setting SHALL stop the API, naming the setting but not its value. The bucket SHALL be private: no
object SHALL be readable without the API's credentials, and the API SHALL never hand storage
addresses or credentials to the browser. The migrate command SHALL create the bucket when it does
not exist, and SHALL leave an existing bucket unchanged. Each module SHALL keep its files under its
own name prefix, and SHALL log stored names and sizes only, never file contents or personal data.
The local environment SHALL start the storage with the other services and SHALL configure the API
for it from `.env.example`.

#### Scenario: Missing storage setting
- **WHEN** the API starts without the storage bucket setting
- **THEN** the process exits with a non-zero code and logs which setting is missing, without any credential value

#### Scenario: Bucket created by migrate
- **WHEN** the migrate command runs against a storage without the configured bucket
- **THEN** the bucket exists afterwards, and running the command again succeeds without changing it

#### Scenario: Anonymous access to the bucket
- **WHEN** a client without credentials requests an object directly from the storage
- **THEN** the storage refuses the request

### Requirement: Stored file cleanup
The API SHALL periodically erase stored files that no record references, so files left behind by a
failed erasure or a failed write never stay longer than 24 hours (BR-14, SEC-08). Each module SHALL
declare which of its stored files are referenced. The cleanup SHALL keep files written less than
one hour earlier, so a write in progress is never erased. When it cannot read the references, it
SHALL erase nothing. Each run SHALL log how many files it erased, without their contents or the
personal data they belong to.

#### Scenario: Orphan file erased
- **WHEN** the cleanup runs and finds a file that is more than one hour old and that no record references
- **THEN** the file is erased and the run logs the count

#### Scenario: References unavailable
- **WHEN** the cleanup runs while the database is unreachable
- **THEN** no file is erased
