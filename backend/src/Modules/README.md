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
4. Copy both `.csproj` files in `backend/Dockerfile` (restore layer), or the image build fails.

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

## Conventions shared by modules (from `add-federation-catalog`)

- **Talking to another module**: only through its `.Contracts`. Read contracts return small
  records, never entities or security data (e.g. `IdentityAccess.Contracts.IUserDirectory` returns
  `UserSummary` for other modules to validate and show users). A module that needs to veto an
  operation of another module implements that module's contract interface (e.g.
  `FederationCatalog.Contracts.ICatalogUsage`, asked before a comparsa or weapon model is deleted).
- **Problem responses**: `SharedKernel.Http.ProblemResults` (`Problem`, `NotFound`, `Conflict`,
  `Invalid`) with a stable `code` the UI translates; each module owns its code constants.
  Out-of-scope comparsa data is answered with `NotFound` (BR-12).
- **Enum codes**: enums exchanged with the API or stored in the database declare a code on every
  member with `[JsonStringEnumMemberName("CODE")]` and use `[JsonConverter(typeof(CodeEnumConverter<T>))]`
  (no integers). `SharedKernel.Codes.EnumCodes` parses and lists them, and
  `SharedKernel.Persistence.EnumCodeConverter<TEnum>` stores them, so JSON, database check
  constraints and validation share one source (`CodedEnumsTests` checks every coded enum). Request
  DTOs take codes as `string?` and parse them with `EnumCodes.FromCode`, so an invalid value is
  reported by field name and case or whitespace variants are rejected.
- **Case-insensitive uniqueness**: Npgsql has no expression indexes, so use a stored generated
  `text` column (`HasComputedColumnSql("lower(name)", stored: true)`, required) with a unique
  index, and name the index so the service can map a `UniqueViolation` to its problem code.
  Normalise input to NFC first. `lower()` needs a UTF-8 ctype (see `docs/development.md`).
- **Model drift**: `ModelDriftTests` fails when a context's model changed without a migration;
  add the new context there.
