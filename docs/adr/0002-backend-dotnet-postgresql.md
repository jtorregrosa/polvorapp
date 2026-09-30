# 0002. Backend: ASP.NET Core + PostgreSQL + EF Core

- Status: Accepted
- Date: 2026-09-30

## Context

The maintainer is proficient in .NET, Node, Angular and React. The domain is form- and
rule-heavy (validation, workflows, exports, scheduled notifications) and handles sensitive data.
Hosting must be cheap and container-based; the repository is public.

## Decision

- **ASP.NET Core** on the current **.NET LTS** release at bootstrap time, Minimal APIs grouped per module.
- **PostgreSQL** (current stable) with **EF Core** and code-first migrations.
- REST + **OpenAPI** document; the frontend TypeScript client is generated from it.
- Validation with FluentValidation (or built-in validation) mirroring business rules BR-xx.
- Testing: xUnit, **Testcontainers** for integration tests against real PostgreSQL, architecture tests for module boundaries.
- Synthetic seed data generator (e.g. Bogus) for development and demos — never real data (SEC-11).

## Consequences

- Strong typing end to end, mature tooling, built-in Identity with 2FA (ADR-0004).
- PostgreSQL is free, container-friendly and available on any VPS or managed provider.

## Alternatives considered

- **Node (NestJS)** — equally valid; .NET chosen for Identity/2FA out of the box and strong document-generation libraries.
- **SQL Server** — licensing and hosting cost; no advantage here.
