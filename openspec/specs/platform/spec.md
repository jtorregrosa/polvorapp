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
database is reachable. Neither response SHALL contain connection strings, host names, exception
messages or personal data.

#### Scenario: API and database are up
- **WHEN** a client calls `GET /api/health/ready` and the database accepts connections
- **THEN** the API responds `200 OK` with status `Healthy`

#### Scenario: Database is unreachable
- **WHEN** a client calls `GET /api/health/ready` and the database does not accept connections
- **THEN** the API responds `503 Service Unavailable` with status `Unhealthy`
- **AND** the body names the failing check (`database`) without exception details or connection information

#### Scenario: Liveness ignores dependencies
- **WHEN** a client calls `GET /api/health/live` while the database is unreachable
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

### Requirement: Anonymous surface is limited until identity exists
Until the identity-access capability is introduced, the API SHALL expose only the health, system
information and API-description endpoints, and none of them SHALL read or return personal data.
There are no Admin or FiringChief roles yet; role and comparsa scoping (BR-12) do not apply to
these endpoints.

#### Scenario: Unknown API route
- **WHEN** a client calls an API path that is not defined, such as `GET /api/arquebusiers`
- **THEN** the API responds `404 Not Found` with a problem-details body

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
that disallows framing (`frame-ancestors 'none'`) and inline scripts, and a `Permissions-Policy`
that allows the camera only for the application's own origin (NFR-01 photo capture). The API
SHALL only be reachable from the same origin as the UI; it SHALL NOT enable cross-origin requests.

#### Scenario: Headers on the UI
- **WHEN** a browser loads the UI through the web entry point
- **THEN** the response includes the four security headers

#### Scenario: Cross-origin request
- **WHEN** a request to the API arrives with an `Origin` header of another site
- **THEN** the response carries no `Access-Control-Allow-Origin` header

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
- **WHEN** the seed command runs twice against two empty databases
- **THEN** both databases contain identical data

### Requirement: Application shell
The UI SHALL render a shell on every route consisting of a header (application name and language
switcher), a main content area and a footer showing the API version obtained from the system
information endpoint. Unknown routes SHALL render a translated not-found page inside the shell.
The shell SHALL be usable from 360 px wide screens upwards (NFR-01), keyboard-navigable with a
visible focus indicator and SHALL have no automatically detectable WCAG 2.1 AA violations (NFR-07).

#### Scenario: Home route
- **WHEN** a user opens the application root
- **THEN** the shell renders with the application name, the language switcher and the footer version

#### Scenario: API unavailable
- **WHEN** the system information endpoint fails
- **THEN** the shell still renders and the footer shows a translated "version unavailable" text

#### Scenario: Unknown route
- **WHEN** a user navigates to a route that does not exist
- **THEN** a translated not-found page with a link back to the start page is shown inside the shell

#### Scenario: Accessibility check
- **WHEN** the shell and the not-found page are scanned by an automated accessibility checker
- **THEN** no WCAG 2.1 A or AA violations are reported

### Requirement: Switch UI language (UC-27)
The UI SHALL be available in `es-ES`, `ca-ES-valencia` and `en`, and the language switcher SHALL
change every visible text at runtime without reloading the page. On first visit the UI SHALL use
the browser's preferred language when it is supported, otherwise `es-ES`. The chosen language SHALL
be remembered in the browser across visits, SHALL be sent to the API as `Accept-Language`, and the
document `lang` attribute SHALL always match the active language. Dates and numbers SHALL be
formatted for the active language. Each language SHALL be listed in the switcher by its own name.

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
(ADR-0006, ADR-0011).

#### Scenario: Fresh clone
- **WHEN** a developer clones the repository, copies `.env.example` to `.env` and starts the environment
- **THEN** the UI shell loads in the browser and the footer shows the API version
- **AND** the readiness endpoint reports `Healthy`

### Requirement: Installable application
The UI SHALL provide a web app manifest and a service worker so that it can be installed on
phones and desktops (NFR-03). The service worker SHALL cache only the application's static assets
and SHALL NOT cache any `/api` response.

#### Scenario: API responses are never cached
- **WHEN** the installed application calls an `/api` endpoint
- **THEN** the response is fetched from the network, not from the service-worker cache

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
