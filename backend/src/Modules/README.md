# Modules

One folder per OpenSpec capability (ADR-0001; change `bootstrap-platform`, design D2):

```
Modules/<Name>/PolvorApp.<Name>/            implementation
Modules/<Name>/PolvorApp.<Name>.Contracts/  public contract: DTOs, service interfaces, events
```

## Adding a module

1. Create both projects under `Modules/<Name>/` and add them to `PolvorApp.slnx`.
2. Reference the implementation project from `PolvorApp.Api` and register its `IModule` in the
   single `AddModules(...)` call in `Program.cs`.
3. Keep implementation types `internal` (a convention, not checked by tests; the compiler then
   stops other modules from using them). Only the `.Contracts` project is public surface.

## Rules (enforced by `tests/PolvorApp.ArchitectureTests`)

1. A module references only `PolvorApp.SharedKernel`, its own `.Contracts` project and other
   modules' `.Contracts` projects — never another module's implementation or the API host.
2. A `.Contracts` project references only `PolvorApp.SharedKernel`.
3. `PolvorApp.SharedKernel` references no module and not the API host.
4. The API host references every module.

The tests read the `ProjectReference` items of every project under `src/`, so a forbidden
reference fails even before any of its types is used.

## Persistence (from `add-identity-access`)

- One `DbContext` per module and one PostgreSQL schema per module, registered with
  `services.AddModuleDbContext<TContext>(schema, migrationOrder)` (`PolvorApp.SharedKernel.Persistence`).
  It uses the host's shared `NpgsqlDataSource`, snake_case names, a migrations-history table in
  the module's schema and the append-only audit guard.
- Migrations live in `Persistence/Migrations` and are generated with a design-time factory
  (`IDesignTimeDbContextFactory`, see `AuditPrivacy`). The host `migrate` command applies every
  module's migrations in `migrationOrder`; the web API never migrates on startup.
- **Audit trail** (SEC-05): every write is recorded with `IAuditTrail.Record(context, record)`
  before `SaveChangesAsync`, so the entry commits in the same transaction as the change. Each
  module context calls `modelBuilder.AddAuditTrail()` (the table is excluded from its migrations;
  the `AuditPrivacy` module owns it). Audit data never contains passwords, codes or tokens.
