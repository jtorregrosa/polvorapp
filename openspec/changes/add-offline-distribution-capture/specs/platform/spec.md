# Spec Delta

## MODIFIED Requirements

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
