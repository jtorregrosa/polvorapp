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

## Conventions shared by modules (from `add-arquebusier-registry`)

- **Reading another module's reference data**: through its read contract, never its tables. For
  example, `FederationCatalog.Contracts.ICatalogDirectory` returns `ComparsaSummary` and
  `WeaponModelSummary` so other modules can validate and show comparsas and weapon models. It
  applies no comparsa scope: the caller enforces BR-12.
- **Foreign keys into another module's schema** are allowed only to protect a reference against
  deletion, and only one way, from a later module to an earlier one in migration order (e.g.
  `registry.arquebusiers.comparsa_id` → `catalog.comparsas`). EF cannot model them, so the
  migration adds them with `migrationBuilder.Sql` and a literal constraint name, and a database
  test asserts they exist. Use `ON DELETE NO ACTION`: it raises `foreign_key_violation` (23503),
  which the owner maps to its `inUse` problem; `RESTRICT` raises `restrict_violation` (23001).
  Case-insensitive uniqueness may also be enforced by storing a normalised value (e.g. the
  upper-cased ownership guide) with a check constraint, instead of a generated key column.
  The referencing module still implements the owner's veto contract (e.g. `ICatalogUsage`), so
  the normal path never relies on the exception. No code queries the other schema.
- **Shared field validation**: `SharedKernel.Validation.InputFields` (trimmed NFC text without
  hidden characters, coded enums) and `SharedKernel.Time.FederationCalendar.Today` (the date in
  Europe/Madrid for "not in the future" and "expired" rules).
- **Optimistic concurrency** (the convention new modules follow; the registry is the first): a row
  edited by several users carries PostgreSQL `xmin` as a `uint` `Version` (`IsRowVersion()`),
  exposed as `version` and required on `PUT`; `DbUpdateConcurrencyException` becomes a `409` with
  a `<entity>.modified` code. Lock rows with `SqlQuery<int>("SELECT 1 AS \"Value\" … FOR UPDATE")`
  (scope in the `WHERE` clause, see `RegistryLocks`) and load them with LINQ afterwards:
  `SELECT *` omits the `xmin` system column.

## Conventions shared by modules (from `add-arquebusier-photos`)

- **Files** go to the private object storage through `SharedKernel.Storage.IObjectStorage`
  (ADR-0005), never to the database or the local disk. Each module owns one key prefix per
  collection, `<module>/<collection>/`, and names objects with a random UUIDv7
  (`registry/photos/<uuid>.jpg`): a key never contains or derives from personal data. Logs carry
  keys and sizes only. Prefixes in use:
  - `registry/photos/` (arquebusier photos, JPEG);
  - `catalog/logos/` (comparsa logos, PNG with transparency; `add-comparsa-logos`).
- **Uploads** are read with `SharedKernel.Http.ImageUploads`: one `file` part of a
  `multipart/form-data` body, read in memory, with the endpoint's request size limit set to
  `ImageUploads.MaxRequestBytes`. Uploads that decode an image use a per-user rate limit
  (`RateLimitPolicies.ImageUploads` for logos).
- **Spreadsheet uploads** (`add-registry-import`) are read with `SharedKernel.Http.SpreadsheetUploads`:
  the `file` part and short text fields, in memory, never stored, with the request size limit set
  to `SpreadsheetUploads.MaxRequestBytes`. The registry checks the package before ClosedXML loads
  it (`Import/WorkbookPackage`): real decompressed bytes, XML depth, document types and the number
  of rows, cells and texts, because a hostile workbook can otherwise exhaust memory or overflow the
  stack. At most two workbooks are read at a time (`ImportSlots`), and the endpoints use the
  per-user `RateLimitPolicies.SpreadsheetImports`. Cell values are personal data: log counts and
  reasons only. Imports run one at a time under a transaction-scoped advisory lock
  (`RegistryLocks.LockImportsAsync`, key "Polvor" + 'I'), bounded by the 5 s lock timeout.
- **Write order**: put the new object, then commit the record that references it, then delete the
  object it replaced. **Delete order**: commit the removal of the reference, then delete the
  object. Every failure then leaves an unreferenced object, never a reference without an object.
  Deleting after commit is best-effort.
- **Cleanup**: the module implements `SharedKernel.Storage.IStoredObjectOwner` for its prefix
  (registered as scoped). The platform's `StoredObjectSweeper` asks it, a page of 500 keys at a
  time, which objects are referenced, and deletes the others once they are more than one hour
  old. A failure deletes nothing.
- **Images** are normalised with `SharedKernel.Images.IImageNormalizer` before they are stored:
  decoded, turned upright, checked against the module's rules, re-encoded and stripped of metadata
  (SEC-12). Never store the uploaded bytes as they came.
- **Outages**: `StorageUnavailableException` becomes `503` with the `storage.unavailable` code.
  Operations that do not need a file must not call the storage before their commit, so they keep
  working while it is down.

## Conventions shared by modules (from `add-compliance-insights`)

- **A module without data**: `ComplianceInsights` owns rules, not tables. It has no DbContext, no
  schema and no migrations. It reads the registry through `ArquebusierRegistry.Contracts.IArquebusierFacts`
  and the catalogue through `ICatalogDirectory`.
- **Contracts in both directions**: the registry calls
  `ComplianceInsights.Contracts.IComplianceRules` to attach warnings to its list and detail, while
  the compliance module reads the registry through its read contract. Both are contract references,
  never implementation references, so the dependency rules hold. The same pattern exists between
  identity and the catalogue (`IFiringChiefAssignmentSource`).
- **Derived values with a reference date**: rules that depend on "today" take the date as a
  parameter. Callers pass `FederationCalendar.Today(TimeProvider)`, and later modules can pass the
  festival dates instead. Never store a derived value such as the age, the license status or a
  warning.

## Conventions shared by modules (from `add-festival-editions`)

- **Locks that other writes respect**: the registry lock (BR-10) is one row,
  `registry.registry_settings`. Every registry write a FiringChief can reach opens its transaction
  with `RegistryWriteGuard.BeginWriteAsync`, which reads the lock `FOR SHARE` for FiringChiefs and
  ends the write as `registry.locked` (409). Locking takes the row `FOR UPDATE`, so it waits for
  writes in flight, and no FiringChief write commits after it. A new FiringChief write path must
  use `guard.BeginWriteAsync` (Admin-only paths may keep `db.BeginWriteAsync`).
  `RegistryLockGuardTests` lists every FiringChief write route and fails for an unlisted one.
- **Money**: amounts are `decimal` in C#, `numeric(p,2)` in PostgreSQL and JSON numbers in the API.
  They are validated, never rounded: more than two decimals is a field error. Responses carry two
  decimals.
- **"At most one" rules**: a partial unique index enforces them, e.g. a single edition in
  progress (`ux_festival_editions_in_progress`, a unique index on `status` filtered to one value).
  A plain query before saving only builds the message; the index is the authority under concurrency.
- **Write guards per module**: a module with concurrent writes runs each one through a guard
  (`RegistryWriteGuard`, `EditionWriteGuard`). The guard turns lock timeouts and deadlocks into a
  retryable 503 and logs every rejection at Warning, with ids only.
