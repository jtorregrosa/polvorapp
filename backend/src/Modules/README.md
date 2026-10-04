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
  An action that writes nothing, such as an export, has no transaction to join: it uses
  `SharedKernel.Auditing.IAuditLog.RecordAsync`, which saves the entry on its own and throws when it
  cannot, so the caller returns nothing unaudited (from `add-exports`).

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
- **References that must outlive their target** (from `add-comparsa-orders`, design D3) use
  `ON DELETE SET NULL` instead. PostgreSQL then nulls the link in the same transaction as the
  owner's deletion, which never fails on a constraint because of it. It can still wait for row
  locks held by the referencing module, which ends as a retryable 503 after the lock timeout. The referencing module keeps its own copy
  of what it needs (e.g. an edition entry's name, national ID and weapon data) and treats a null
  link as "no longer there". Its copy columns are nullable only so that a GDPR erasure can blank
  them. Example: `orders.edition_entries.arquebusier_id` → `registry.arquebusiers`.
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
  - `catalog/logos/` (comparsa logos, PNG with transparency; `add-comparsa-logos`; and the Federation
    logo, `add-distribution-planning`).
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

## Conventions shared by modules (from `add-billing-summary`)

- **Pricing**: `Billing` is the second module without data (no `DbContext`, schema, migrations or
  endpoints). It owns the pricing rule behind `Billing.Contracts.IBillingCalculator`: an order's
  `BillingQuantities` and the edition's `BillingPrices` become a `BillingSummary` (four lines,
  total, provisional or final, missing prices). Modules that show or export amounts call it and
  never multiply prices by hand, so the order page, the overview and the exports cannot disagree.
- **Derived values travel with their source**: the billing is not served by endpoints of its own.
  The orders compute it from the same `OrderTotals` they return and add it to their responses, so
  it inherits their scope (BR-12) and `DRAFT` rules and is always consistent with the totals.

## Conventions shared by modules (from `add-exports`)

- **Documents**: `Exports` is a module without a schema. Each export is a named, versioned
  `IExportDefinition` (ADR-0008), a pure function of the data a loader reads once per request
  (`ExportData`), returning an `ExportTable` (typed cells, validated) that one Excel writer and one
  PDF writer render. A recipient's real template changes only its definition and golden files.
- **Personal data in files**: definitions hold only the columns their recipient needs (SEC-06);
  files are generated per request, never stored, and audited with `IAuditLog` before they are sent.

## Conventions shared by modules (from `add-distribution-planning`)

- **Documents of other modules**: a module that prints a document builds its content itself — a
  `DocumentTable` (Excel and PDF) or a `DocumentForm` (one PDF page to sign) from
  `Exports.Contracts` — and renders it with `IDocumentRenderer`, which reuses the exports' writers,
  fonts and QuestPDF settings. The module still checks who may have the file, audits it with
  `IAuditLog` before returning it, and never stores it. `DocumentColumn.ForHandwriting` marks a column
  left empty to be filled in on paper. File names are built with `SharedKernel.Text.FileSlug` from
  names that are not personal data.
- **Reading another module's entries**: `ComparsaOrders.Contracts.IEditionEntries` returns
  `EditionEntryFacts` (the entry, its order, edition, comparsa and order status, what it collects
  and the identity copy) for one comparsa's order or by id. Like every read contract it applies no
  scope: the caller enforces BR-12.
- **Run-time assets that must never be committed**: the Federation's logo is uploaded by an Admin
  into the private storage (`/api/federation-logo`), through the same `LogoUploadFlow` as comparsa
  logos, and read by document modules with `ICatalogDirectory.ReadFederationLogoAsync`. A module
  printing it treats `StorageUnavailableException` as a failed document (`503`), never as a missing
  logo; null means none was uploaded. The repository is public, so no crest or brand asset of the
  Federation is ever added to it, its seed or its tests.
- **Single-row settings**: a table whose one row the migration inserts and a check constraint keeps
  single (`catalog.federation_settings`, `id = 1`), locked with `FOR NO KEY UPDATE` for changes; no
  unique index or upsert is needed.
- **Dependent rows across schemas** use `ON DELETE CASCADE`: a row that means nothing without its
  target goes with it in the same transaction. Example: `distribution.pickup_proxies.holder_entry_id`
  and `proxy_entry_id` → `orders.edition_entries`. Entries are removed only by an arquebusier's
  deletion while the orders are open (BR-14), which the registry audits with the removed entry ids,
  so the cascade needs no audit entry of its own. A database test asserts the key, its delete rule
  and the cascade. Use it only when keeping the row would be wrong; `SET NULL` with a copy is the
  default for history.

## Conventions shared by modules (from `add-notifications`)

- **Notification outbox**: a module whose change should notify someone records a
  `Notifications.Contracts.NotificationEvent` with `INotificationOutbox.Record(context, event)`
  before `SaveChanges`, so the event commits or rolls back with the change and its audit entry. Its
  context maps the table with `modelBuilder.AddNotificationOutbox()` (excluded from its migrations;
  the `Notifications` module owns `notifications.notification_events`), and a snapshot-only migration
  records the mapping. Events hold identifiers only — never names, reasons or other free text. The
  notifications module works out the recipients, renders and sends the emails in the background, and
  re-checks recipients and facts when it sends. Record only events that changed something: an
  operation that writes nothing records nothing.
- **Recipients** come from read contracts: `IUserDirectory.ListAsync(role)` (with each user's status
  and locale) and `ICatalogDirectory.ListFiringChiefAssignmentsAsync()` (assignments to active
  comparsas). Like every read contract they apply no scope; the notifications module applies BR-12.

## Conventions shared by modules (from `add-audit-privacy`)

- **Audit action catalogue**: every action code a module records is a constant in its
  `<Module>AuditActions` class and is declared there with its entity type and retention class
  (`AuditActionDefinition`), registered with `services.AddAuditActions(<Module>AuditActions.All)`.
  `AuditTrail` refuses to record an undeclared code, or a code under another entity type, so a new
  action fails its module's tests until it is declared. Access and security events (sign-ins,
  lookups) use `AuditRetentionClass.Security` (kept 1 year); everything else is `Standard` (5 years).
  Every code also needs an `audit:actions.<Code>` label in the three locales (a frontend test checks it).
- **Record links in the audit log**: a module whose records have a page registers
  `services.AddAuditRecordResolver<TContext, TEntity>(EntityType)` for entities keyed by a GUID `Id`,
  so the audit log links an entry to its record only while it exists.
- **Audit entries are never changed**: a database trigger refuses `UPDATE`, `DELETE` and `TRUNCATE`.
  Only `AuditPrivacy`'s `AuditMaintenance` deletes expired entries or redacts personal values, and an
  architecture test forbids naming the guard or writing to the table anywhere else.
- **Personal data participants** (GDPR requests, UC-26): a module that stores a person's data, or
  links to a person, implements `AuditPrivacy.Contracts.IPersonalDataParticipant` and registers it as
  a scoped service. It describes, exports (only the subject's own data; another person appears by
  role) and erases its part. The erasure runs on the request's transaction in two steps, in the order
  of `PersonalDataParticipantOrder`: `PrepareErasureAsync` locks and notes in `PersonalDataErasure`
  what later participants need, then `EraseAsync` deletes or anonymises and adds its counts. A
  participant enlists its context with `db.EnlistAsync(transaction)` (SharedKernel) or runs SQL on the
  transaction's connection, never commits, and refuses a blocking rule with
  `PersonalDataErasureRefusedException`. `PersonalDataCoverageTests` fails when a module with a column
  such as `national_id`, `first_name`, `email` or `user_id` registers no participant.

## Conventions shared by modules (from `add-badges`)

- **Card documents**: `Exports.Contracts.DocumentBadgeSheet` is the arquebusier badge layout (ID-1
  cards, 2 × 5 on an exact 210 × 297 mm page, crop marks), rendered by
  `IDocumentRenderer.RenderBadgeSheet`. The calling module picks the words, values and order; the
  writer owns the geometry and the Federation green. A value that does not fit shrinks and wraps,
  and is never cut.
- **Images in documents**: `DocumentImage.FromPng` and `DocumentImage.FromJpeg` read the size from
  the image header (1 to 4096 px a side, at most 4 MB); `FromJpeg` takes only complete 8-bit grey or
  colour baseline, extended or progressive JPEGs, which the writers draw as they are. QuestPDF resamples images to 288 dpi by
  default, so a photo already scaled for print is drawn with `UseOriginalImage()`.
- **The Federation's name** printed on documents comes from `Exports.Contracts.FederationNames`
  (`Spanish`, `Valencian`; English documents use the Spanish proper name), so documents never drift.
- **ID photos for documents**: `ArquebusierRegistry.Contracts.IIdPhotoReader.ReadAsync` returns the
  stored JPEG, read again if it was replaced meanwhile, or null when there is none, or its image is
  gone, larger than 4 MB or not a JPEG (logged as an error). The read never goes past the limit. It is unscoped: only Admin-only callers may use it. It throws
  `StorageUnavailableException`, which the caller answers with `503`, never with a missing photo.
  `IArquebusierRoster.FindManyAsync` may take request ids on Admin-only routes, for the same reason.
