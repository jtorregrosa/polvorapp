# 0001. Modular monolith, single deployable backend

- Status: Accepted
- Date: 2026-09-30

## Context

PolvorApp is a small system (~800 arquebusiers, ~60 users) maintained by one developer
(NFR-09). The domain has clear areas (registry, editions/orders, distribution, exports,
notifications) that must stay decoupled so they can evolve independently and map to OpenSpec
capabilities.

## Decision

- One backend deployable (API) and one frontend (SPA), plus a database and object storage.
- Inside the backend, code is organised **by module = OpenSpec capability** (vertical slices),
  each with its own endpoints, application logic and persistence mapping. Modules talk through
  explicit application services, not through each other's tables.
- A single PostgreSQL database with one schema per module where practical.
- Background work (email notifications, scheduled alerts) runs in-process as hosted services.

## Consequences

- Simple to run locally, deploy, debug and back up.
- Module boundaries keep specs and code aligned; a module could be extracted later if ever needed.
- Requires discipline (architecture tests) to prevent cross-module coupling.

## Alternatives considered

- **Microservices** — operational overhead unjustified for this size and a single maintainer.
- **Layered monolith (Controllers/Services/Repositories)** — tends to mix domains; harder to map to specs.
