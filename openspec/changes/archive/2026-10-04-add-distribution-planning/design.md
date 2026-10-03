# Design

## Context

See `proposal.md` for the motivation and `specs/distribution/spec.md` for the behaviour. The facts
below come from the current code and shape the approach.

- **Modules** (ADR-0001, `backend/src/Modules/README.md`): one module per capability, one schema per
  module, other modules read only through `.Contracts`. Cross-schema keys exist only to protect a
  reference (`ON DELETE NO ACTION`, with the owner's veto contract) or to outlive it (`SET NULL`).
- **Editions.** `IEditionDirectory` gives `EditionSnapshot` (year, status, festival dates) and
  `ReadForOrderWriteAsync`, which reads the edition `FOR SHARE` on the caller's transaction so a
  status move waits for writes in flight. `IEditionUsage` vetoes the deletion of a draft edition.
- **Orders.** `IOrderExports.ListValidatedAsync` returns the validated orders with every entry
  (status, powder, weapon source, rental model, flask, history copy, arquebusier id). There is no
  contract to read one comparsa's entries for a picker, or entries by id. Entries are removed only
  by `OrdersDeletionParticipant` (BR-14), inside the registry's deletion transaction.
- **Registry.** `IArquebusierRoster.FindManyAsync` returns live names, DNI/NIE and
  `ArquebusierLicenseFacts` (`Pending` | `Issued(Type, ExpiresOn, …)`).
- **Catalogue.** `ICatalogDirectory` gives comparsa names and model labels, and
  `ReadComparsaLogoAsync` → `ComparsaLogoImage(Png, Width, Height)` for documents; `ICatalogUsage`
  vetoes comparsa deletion. Logos go through `Logos/` (`ComparsaLogoAdministration`, `LogoStorage`,
  `LogoReader`, `CatalogLogoOwner` for the orphan sweep) under the `catalog/logos/` prefix.
- **Scope.** `IComparsaScope.GetAccessAsync` gives the user's comparsas (all for Admins).
- **Documents.** `Exports` owns ClosedXML, QuestPDF, the embedded Geist fonts, `XlsxExportWriter`,
  `PdfExportWriter`, the internal `ExportTable`, the comparsa slug and the golden-file tests. Its
  `.Contracts` project is empty, reserved for this reuse (add-exports, design D1).
  `SharedKernel.Auditing.IAuditLog` audits actions that write nothing; `RateLimitPolicies.Exports`
  limits downloads per user.
- **Frontend.** `features/exports/components/DownloadButtons` (Excel + PDF pair, pending state,
  translated failure); `components/app` has `DateInput` but no time input; navigation is data
  (`app/navigation.ts`).

## Goals / Non-Goals

**Goals:**
- Distribution rules, numbering and document contents live in the `Distribution` module; rendering
  stays in one place (`Exports`), reached through a contract.
- The absence rules and the one-proxy-per-holder rule hold under concurrent requests.
- Lists and forms are deterministic from synthetic data and covered by golden files.

**Non-Goals:**
- Locking orders while proxies are written: entry changes are tolerated and surface as the derived
  "does not apply" problem.
- A generic form designer: one form layout, enough for the authorisation (and reusable later only
  if it fits).

## Decisions

### D1. A `Distribution` module with a `distribution` schema

`Modules/Distribution` (`PolvorApp.Distribution`, `.Contracts`) owns days, slots and proxies, their
endpoints, rules, list and form contents, and audit. It references the contracts of
`FestivalEditions`, `FederationCatalog`, `ComparsaOrders`, `ArquebusierRegistry`, `IdentityAccess`
and `Exports`. Its migration runs after `ComparsaOrders` (its keys point to orders, editions and
the catalogue). It implements `IEditionUsage` (an edition with distributions or proxies is in use)
and `ICatalogUsage` (a comparsa with a slot or proxy is in use). `.Contracts` holds only the coded
enum `DistributionType`; no other module needs to read distributions yet.

*Alternative:* add the lists and the form to `Exports` and the data to `ComparsaOrders`. Rejected:
distribution would be split over two modules that are not its own (ADR-0001), and orders would grow
rules that do not concern them.

### D2. Data model and migration

```
distribution.distributions
  id uuid PK, edition_id uuid NOT NULL → editions.festival_editions (NO ACTION),
  type text NOT NULL CHECK (POWDER|WEAPONS), date date NOT NULL, location text NOT NULL,
  updated_at timestamptz NOT NULL, xmin → Version
  ux_distributions_edition_type UNIQUE (edition_id, type)

distribution.distribution_slots
  distribution_id uuid → distributions (CASCADE), comparsa_id uuid → catalog.comparsas (NO ACTION),
  starts_at time NOT NULL, PK (distribution_id, comparsa_id)

distribution.pickup_proxies
  id uuid PK, edition_id uuid → editions (NO ACTION), comparsa_id uuid → catalog.comparsas (NO ACTION),
  type text NOT NULL CHECK (POWDER|WEAPONS),
  holder_entry_id uuid NOT NULL → orders.edition_entries (CASCADE),
  proxy_entry_id  uuid NOT NULL → orders.edition_entries (CASCADE),
  created_at timestamptz NOT NULL, CHECK (holder_entry_id <> proxy_entry_id)
  ux_pickup_proxies_holder_type UNIQUE (holder_entry_id, type)
  ix_pickup_proxies_edition_comparsa (edition_id, comparsa_id), ix_pickup_proxies_proxy_entry (proxy_entry_id)
```

- `starts_at` is a `time` (`TimeOnly`): the date is the distribution's. The data model's
  `startsAt` is documented as a time of day.
- A proxy copies `edition_id` and `comparsa_id` from the holder's entry when it is registered. An
  entry never changes order (BR-13), so they stay true, and they make scope checks and listing a
  single-table query.
- **New convention — dependent rows across schemas use `ON DELETE CASCADE`.** A proxy means nothing
  without both entries. Entries are removed only by an arquebusier's deletion (BR-14), which is
  audited by the registry with the removed entry ids; the cascade removes the proxies in the same
  transaction and never fails it. A database test asserts the keys and the cascade. Documented in
  the modules README.
- The cross-schema keys are added with `migrationBuilder.Sql` and literal names, as the existing
  modules do; `ModelDriftTests` gains the new context.

*Alternative for the cascade:* implement `IArquebusierDeletionParticipant` and delete the proxies
by hand. Rejected: the orders participant already removes the entry; the cascade expresses the same
fact with no ordering between participants, and with `NO ACTION` the deletion would fail.

### D3. Contracts the module reads

- `ComparsaOrders.Contracts.IEditionEntries` (new, unscoped — the caller enforces BR-12):
  - `ListAsync(editionId, comparsaId)` → the comparsa's order entries (empty when not prepared);
  - `FindManyAsync(entryIds)` → entries by id;
  - both as `EditionEntryFacts(EntryId, OrderId, EditionId, ComparsaId, OrderStatus, ArquebusierId?,
    IsActive, PowderKg, WeaponSource, RentalWeaponModelId?, Flask, ExportedPerson Copy)`, with
    `ToString()` printing no personal data, like `ExportedEntry`.
- The lists read the validated orders through the existing `IOrderExports.ListValidatedAsync`: a
  proxy entry belongs to the same order as its holder, so it is already in the result.
- `Exports.Contracts.IDocumentRenderer` (new; implemented in `Exports`):
  - `RenderedDocument RenderTable(DocumentTable table, DocumentFileFormat format)` — Excel or PDF;
  - `RenderedDocument RenderForm(DocumentForm form)` — PDF;
  - `RenderedDocument(ReadOnlyMemory<byte> Content, string ContentType, string FileName)`, so callers
    never repeat content types or extensions. `DocumentFileFormat` (not `DocumentFormat`, which
    collides with OpenXML's root namespace) and `DocumentCellType.Number` (CA1720 refuses `Integer`
    on a public enum) are the public names.
  `DocumentTable`, `DocumentColumn`, `DocumentCellType` and `DocumentFileFormat` are the current
  internal `ExportTable` types made public and moved, with the same validation; the exports use
  them unchanged, so their golden files must not change. `DocumentColumn` gains
  `ForHandwriting` (default false): the PDF gives such a column a fixed width and its rows a
  writable height; Excel ignores it. `DocumentForm` is a title, a few heading lines, labelled
  sections of `(label, value?)` fields — a null value prints a blank line —, statement paragraphs,
  blank labelled lines, signature boxes and an optional PNG logo drawn in the header at a fixed
  height, keeping its shape. The comparsa slug helper moves to
  `SharedKernel.Text` so file names are built the same way.

*Alternative:* reference QuestPDF from `Distribution`. Rejected: a second copy of the font setup and
process-wide settings, and two PDF styles.

### D4. Writes, concurrency and rules

All writes run in a transaction with the module's `lock_timeout`, through a
`DistributionWriteGuard` (as `EditionWriteGuard`): lock timeouts and deadlocks become a retryable
`503 distribution.busy`, and rejections are logged at Warning with ids only.

- **Edition rule.** Each write reads the edition with `IEditionDirectory.ReadForOrderWriteAsync`
  (`FOR SHARE`), so a status move waits and no write commits on an edition that just left
  `IN_PROGRESS`. Days and slots need `IN_PROGRESS`; proxies need `IN_PROGRESS` for FiringChiefs and
  "not `DRAFT`" for Admins.
- **Days.** One per type by `ux_distributions_edition_type`; a `UniqueViolation` on it becomes
  `distribution.alreadyPlanned`. Version from `xmin`. Date and location validated with
  `InputFields`; the date rule uses the snapshot's `Year` and `FestivalEndsOn`.
- **Slots.** `PUT` replaces the set: lock the distribution `FOR UPDATE`, check the version, validate
  every slot (known comparsas through `ICatalogDirectory`, no duplicates), diff against the stored
  set for the audit, replace, and touch `updated_at` so the version changes.
- **Proxies.** Read both entries with `IEditionEntries.FindManyAsync`; check scope, BR-06 (same
  order), not the holder, something to collect, and the proxy's license
  (`ProxyLicenseRule`, D5) on the reference date. Then take a transaction-scoped advisory lock on
  (edition, comparsa) — key "Polvor" + 'X' with the hashed pair, the pattern of `RegistryLocks` —
  and check the absence rules (`proxyAbsent`, `holderIsProxy`) under it; the unique index backs
  `alreadyAuthorised`. Two concurrent registrations in one comparsa therefore serialise; other
  comparsas do not wait. The lock is the two-key `pg_advisory_xact_lock(int, int)`: class `"PolX"`
  and a deterministic FNV-1a hash of both ids (`HashCode` differs between processes). Every insert
  or delete of a proxy takes it first — a removal reads the proxy's edition and comparsa with a plain
  read, then locks, then deletes — and the contract reads (entries, registry) happen before the
  write's transaction, so a write never holds two connections. A `23503` on an entry key (the entry
  was deleted meanwhile, BR-14) answers `404` like an unknown entry.

*Alternative:* lock the entries in the orders schema. Rejected: a module never queries another
schema, and the derived "does not apply" problem covers entry changes.

### D5. Proxy license rule and proxy problems

- `ProxyLicenseRule` (pure) takes the proxy's `ArquebusierLicenseFacts` (or none, when the
  arquebusier left the registry) and the **reference date** — the date of the distribution of that
  type, or `FestivalStartsOn` while it is not planned — and holds only for `Issued` with `ExpiresOn`
  on or after it, of any `LicenseType`. It is **blocking** at registration (`licenseInvalid`), a
  maintainer decision recorded in `docs/open-questions.md` as the exception to "compliance checks
  are warnings".
- `ProxyProblems` (pure) re-evaluates a stored proxy on every read: `NOT_APPLICABLE` (the holder
  has nothing left to collect of that type) and `LICENSE_INVALID` (`ProxyLicenseRule` no longer
  holds). Lists skip a proxy with a problem, the form refuses it, the page shows it. Nothing is
  stored (README: derived values with a reference date).
- The candidates endpoint returns every other entry of the order with an `eligible` flag and the
  reason (`licenseInvalid`), so the panel can show why an entry cannot be chosen.

### D6. Documents

Pure builders in `Distribution/Documents`, unit-tested without a database:

- `DistributionNumbering`: orders comparsas by slot time, then `SpanishOrder` name, slotless last;
  holders by `SpanishOrder` last name and first name; numbers from 1.
- `PowderDistributionList` and `WeaponDistributionList` (name `powder-distribution-list` /
  `weapons-distribution-list`, version `1`, not provisional) build a `DocumentTable`:
  - powder: Nº, Turno, Comparsa, Apellidos y nombre, DNI/NIE, Kg, Cantimplora, Nº cantimplora ✍,
    Trazabilidad 1 ✍, Trazabilidad 2 ✍, Autorizado (name), DNI/NIE autorizado;
  - weapons: Nº, Turno, Comparsa, Apellidos y nombre, DNI/NIE, Modelo, Nº de arma ✍, Autorizado,
    DNI/NIE autorizado (✍ = `ForHandwriting`).
  The headings above are the Spanish ones; each language has its own. The title states the type
  and edition; the heading lines the date, location and that the numbering is valid for this
  print. Identity from the registry while present, the copy otherwise.
- `PickupAuthorisationForm` (version `1`) builds a `DocumentForm`: the Federation's logo when
  uploaded, the Federation's name as text, "Authorisation to collect the powder / the rented
  weapon — Festival {year}" in the user's language, the holder's and the proxy's sections (name,
  DNI/NIE, license type `AE`/`A-PROF` or blank, comparsa), the statement of the current paper
  form, the distribution's date and location when planned, blank lines for the reason and for
  "San Vicente del Raspeig, … {year}", and the two signature boxes.
- `DistributionTexts`: the words in es-ES, ca-ES-valencia and en, chosen by the request's
  `CurrentUICulture` (maintainer decision; as the comparsa list in add-exports). Dates are written
  as `dd/MM/yyyy`; model labels and comparsa names are not translated. File names stay in English.
- `DistributionDocuments` reads the distribution with slots, the validated orders, the edition's
  proxies, the registry identities and licenses, and the catalogue labels; the form also reads the
  Federation logo (D11). The reads go through four modules' contracts on their own connections, so
  they share no snapshot (a deviation from the single repeatable-read loader first planned): a
  change committed in between can leave a proxy without its entry in the list — it is then left
  out, counted in the audit entry (`proxiesLeftOut`) and logged — and a re-download is cheap.

Order per request, as the exports: check permission and state → build → render →
`IAuditLog.RecordAsync` → return the file (`503` and no file when the audit fails).

File names: `polvorapp-{year}-powder-distribution-list.{xlsx|pdf}`,
`polvorapp-{year}-weapons-distribution-list.{…}`,
`polvorapp-{year}-pickup-authorisation-{powder|weapons}-{comparsa-slug}-{first 8 hex of the proxy id}.pdf`.

### D7. API

Under `/api/distribution`, signed-in users; problem codes owned by the module.

| Route | Who | Answer |
|---|---|---|
| `GET /editions/{editionId}` | all (FC: not `DRAFT`) | `DistributionPlanResponse`: the edition (id, year, status), the two days (id, type, date, location, version, slots scoped), Admins also the comparsas without slot and the orders not validated; `canPlan` and `canManageProxies` (the UI explains a `false` from the edition status) |
| `POST /editions/{editionId}/distributions` | Admin | `201` with the day; `400`, `409 distribution.alreadyPlanned` / `.editionNotInProgress` |
| `PUT /distributions/{id}` | Admin | the day; `400`, `409 …modified` / `.editionNotInProgress` |
| `DELETE /distributions/{id}?version=` | Admin | `204`; `409 …modified` |
| `PUT /distributions/{id}/slots` | Admin | the slots; `400 slots[i].…`, `409` |
| `GET /editions/{editionId}/proxies?comparsaId=` | all, scoped | proxies with names, comparsa, type, problems |
| `GET /editions/{editionId}/comparsas/{comparsaId}/proxy-candidates` | all, scoped | holders by type (with something to collect, no proxy of it) and the order's other entries with `eligible` per type and the reason |
| `POST /editions/{editionId}/proxies` | Admin, FC scoped | `201`; `400`, `404`, `409 proxies.*` |
| `DELETE /proxies/{id}` | Admin, FC scoped | `204`; `404`, `409 distribution.editionNotInProgress` |
| `GET /distributions/{id}/list/{format}` | Admin | the list file; rate policy `Exports` |
| `GET /proxies/{id}/form` | Admin, FC scoped | the form PDF in the user's language; `409 proxies.notApplicable` / `proxies.licenseInvalid`; `503 storage.unavailable`; rate policy `Exports` |

Out-of-scope or `DRAFT` (for a FiringChief) answers `404`, unknown formats `404`. `Cache-Control:
no-store` comes from the host on every answer. OpenAPI regenerated; the BR-12 guard test learns the
new comparsa routes.

### D8. Audit actions

Same transaction (`IAuditTrail`): `DistributionPlanned`, `DistributionEdited` (changed fields),
`DistributionDeleted` (snapshot: type, date, location, slot count), `DistributionSlotsChanged`
(per comparsa id: previous and new time), `PickupProxyAuthorised` and `PickupProxyRemoved`
(comparsa id, type, holder and proxy entry ids). Downloads (`IAuditLog`): `DistributionDocumentDownloaded`,
data `{ document, version, format, editionId, editionYear, type, rows | comparsaId }`. No names,
DNI/NIE, locations are not personal data.

### D9. Frontend

- Routes: `distribution` (redirects to the current edition's page, or says there is none) and
  `editions/:editionId/distribution`; a "Distribution" navigation entry (icon `Truck` or similar
  from lucide) for every role; a link on `EditionDetailPage` for non-draft editions.
- `features/distribution/pages/DistributionPage.tsx` (detail template): a `SectionCard` per day
  (`DistributionDaySection`: facts, Admin actions, slots table, list downloads with the
  not-validated warning and the renumbering note) and a `ProxiesSection` (`DataTable`,
  `FilterSelect` by comparsa for Admins, problems as status badges, "Print form" (hidden while a problem holds), "Remove" with
  `ConfirmDialog`).
- Sheets (`EditSheet`): `DaySheet` (date with `DateInput`, location), `SlotsSheet` (one row per
  comparsa with a new `TimeInput` composite, empty = no slot), `ProxySheet` (comparsa, type with
  `RadioCards`, holder and proxy with `SelectInput`, the note that the reason is handwritten).
- New composite `components/app/TimeInput` (native `type="time"` styled with tokens, story, test,
  axe), per ADR-0009.
- `DownloadButtons` (from `features/exports`) gains an optional `formats` prop (`['pdf']` for the
  form) and maps `proxies.notApplicable`.
- i18n: a `distribution` namespace in the three locales (page, days, slots, proxies, problems,
  documents, problem codes); `editions.inUse` copy updated to "orders or distribution days".
- `features/federation-catalog`: a `FederationLogoSection` on the comparsas page for Admins,
  reusing the comparsa logo section's upload, crop, replace and remove parts (generalised to take
  the endpoints), with its keys in the `catalog` namespace.

### D11. Federation logo

- `catalog.federation_settings`: a single row (`id` fixed by a check constraint, `logo_key`,
  `logo_version`, `updated_at`), created by the migration, so "at most one" needs no index.
- `FederationLogoAdministration` reuses `LogoStorage` and `IImageNormalizer` with the comparsa logo
  rules; objects under the existing `catalog/logos/` prefix with a random UUIDv7 name, and
  `CatalogLogoOwner` also reports the Federation logo as referenced, so the sweep keeps it.
  Write and delete order as for comparsa logos.
- Endpoints in the catalogue (next to `/api/comparsas`): `GET /api/federation` (signed-in: the logo's
  version and size, or null), `GET /api/federation-logo` (signed-in, `404 logos.notFound` when none,
  `no-store`), `PUT` (multipart `file`, Admin, `ImageUploads` rate limit) and `DELETE` (Admin); audit
  actions `FederationLogoUploaded` (`{ replaced }`) / `FederationLogoRemoved`, entity
  `FederationSettings` `1`, never the image, its key or size.
- The upload flow of comparsa logos (normalise, put, swap under the owner's row lock, commit, erase
  the replaced image) is extracted into `LogoUploadFlow` and shared by both owners.
- `ICatalogDirectory.ReadFederationLogoAsync()` → `LogoImage?` (`ComparsaLogoImage` renamed, as both
  kinds share it). A storage failure surfaces as `503 storage.unavailable`
  from the form download.
- Nothing in git: no image in the seed, fixtures use generated synthetic PNGs, as the comparsa logo
  tests do. The brand rule ("never commit the Federation's crest") is kept.

*Alternative:* a logo file mounted at deploy time. Rejected: needs host access for every change,
and the comparsa logo pipeline already gives upload, validation, private storage and audit.

### D10. Tests

- Unit: numbering, both list builders (filters, columns, proxies, slotless comparsas, deleted
  arquebusiers, three languages), the form builder (with and without logo, three languages), the
  proxy license rule and problems, slot and day validation.
- Golden files: each list in both formats and the form, from synthetic data, read back as in
  add-exports (ClosedXML grid, PdfPig text); the exports' existing golden files unchanged after the
  `DocumentTable` move.
- Integration (Testcontainers): every route's permissions and scope, the edition rules, the unique
  and absence rules under concurrent requests, the cascade on an arquebusier's deletion, the
  edition and comparsa vetoes, audit entries, `429`, `no-store`.
- Frontend: Vitest + Testing Library + axe for the page, sections, sheets and `TimeInput`.
- E2E: see tasks.

## Risks / Trade-offs

- **[Numbers on a printed list differ from a reprint]** → Derived by decision; the document and the
  page say the numbering is valid for that print. UC-21 will store the number at handover.
- **[A proxy stops holding after an entry or license change]** → Derived `NOT_APPLICABLE` /
  `LICENSE_INVALID` problem, left out of the lists, form refused; the FiringChief removes it.
- **[Planning or moving a day invalidates registered proxies]** → The page shows the problem on
  every affected proxy; the day's save does not block on it (the Admin may move the day).
- **[The Federation's crest leaks into git]** → It exists only through the upload; tests and
  E2E use generated synthetic images; the verification's diff review checks that the change adds
  no image file.
- **[Cross-schema cascade surprises a future maintainer]** → Documented convention, database test
  of the key and the cascade, scenario in the spec.
- **[Moving `ExportTable` breaks the exports]** → The move is a pure refactor checked by the existing
  golden files before any new definition.
- **[Personal data on printed lists]** → Only name, DNI/NIE, kilograms, flask and model (SEC-06);
  generated per request, audited, `no-store`; the privacy notice (Q-50) covers printed lists.
- **[A Valencian or accented name Geist cannot draw]** → `ThrowOnMissingTextGlyphs` already fails
  loudly; Latin Extended covered by existing tests.

## Migration Plan

Two additive migrations: `distribution` (schema, three tables, cross-schema keys) and
`FederationCatalog` (`federation_settings` with its single row). After deploying, an Admin uploads
the Federation logo from the comparsas page. Deploy: run the
host's `migrate`, then the new images. Rollback: redeploy the previous images; the new schema is
unused by them (drop it by hand only if the change is abandoned).

## Research notes (task 1.1)

- **Npgsql 10.0.3 / EF Core 10.0.12**: `TimeOnly` maps to `time without time zone` by default
  (since Npgsql 6), as `DateOnly` maps to `date` — no converter. Cross-schema keys stay hand-written
  SQL in the migration (`… REFERENCES orders.edition_entries (id) ON DELETE CASCADE`), with literal
  constraint names, as the orders migration does for `SET NULL`.
- **PostgreSQL**: `pg_advisory_xact_lock` is released at commit or rollback; its two-key form
  `(int, int)` is a key space apart from the one-key locks of the registry and identity. The module
  uses class `"PolX"` and an FNV-1a hash of the (edition, comparsa) pair. A hash collision only
  serialises two comparsas, never breaks a rule. The wait is bounded by the transaction's
  `lock_timeout`, as `RegistryLocks` and `UserLock` do.
- **QuestPDF 2026.9.1**: `container.Height(h).Image(bytes).FitArea()` draws a PNG at a fixed height
  keeping its shape; `MinHeight(h)` gives table cells a writable height; `Border(1).Padding(…)`
  draws signature boxes; `LineHorizontal(0.5f)` under an empty label draws a writing line.
- **`<input type="time">`**: supported by Chromium, Firefox and WebKit; the value is always `HH:mm`
  whatever the display locale; it must have a `<label>`; Playwright's `fill('09:30')` sets it. The
  composite therefore exchanges `HH:mm` strings, which is what the API takes.

### Review notes (group 2)

`csharp-reviewer`, `type-design-analyzer`, `security-reviewer`: no CRITICAL; two HIGH from the type
review, both fixed. Applied: file stems are checked when a `DocumentTable` or `DocumentForm` is built
(`DocumentFileStem`: lower-case ASCII words joined by single hyphens, at most 120 characters; the
message never repeats the value); `DocumentImage.FromPng` reads the size from the PNG header and
refuses anything but a PNG of 1 to 4096 px a side and at most 4 MB; QuestPDF failures (its
missing-glyph message quotes the letters of a name) become a `DocumentRenderingException` with a
fixed message and no inner exception, for the exports too; the tables and forms check their texts,
keep their own copies of the lists, need at least one column, refuse unknown cell types, keep
handwriting columns textual and empty, and allow text in a number column only in the total row's
first cell; form blocks are sealed classes whose base only the contract assembly can derive (a
record's copy constructor would have allowed it), with a test that every block renders; a form has
one to three signature boxes, kept together with `ShowEntire`; labels are printed as the builder
writes them (colons included); the logo's height and shape are asserted; `ExportService` renders
through `IDocumentRenderer`, so the format, content type and file name have one source;
`IEditionEntries.FindManyAsync` takes at most 1,000 ids, and its documentation spells out the
caller's BR-12 checks (comparsa and edition of each returned entry). `MinHeight` is applied only to
tables with handwriting columns. Kept: entry facts mirror the stored data (a rental's model id is
nullable in the type), and the form may flow to a second page if a builder writes a lot; the
distribution form's one-page test comes with its builder (task 6.3).

### Review notes (group 3)

`csharp-reviewer`, `security-reviewer`: no CRITICAL/HIGH. The comparsa upload flow extracted into
`LogoUploadFlow` was confirmed unchanged (put → swap under the row lock with its audit → commit →
erase the replaced image; the same failure handling). Applied: Federation-specific tests for a held
row lock (busy, nothing stored), two replacements at once (one logo, no orphan) and a failure before
the commit (new image erased); the antiforgery header is required on the new routes; the audit data
is asserted to hold only `replaced`; the modules README lists the Federation logo under the
`catalog/logos/` prefix, documents run-time assets that are never committed (and that a document
treats a storage outage as a failure, not as a missing logo) and the single-row settings table.
Kept: `ComparsaLogo`, `CatalogOutcome.ComparsaNotFound` and `LogoRead.ComparsaNotFound` keep their
names (documented as "any owner"); renaming them would touch the comparsa model snapshot for no
behaviour. Comparsa upload logs now carry `{Owner}` ("comparsa {id}") instead of `{ComparsaId}`; no
alert relies on it. `.claude/settings.local.json` is the maintainer's local file and is never staged.

### Review notes (group 4)

`csharp-reviewer`, `database-reviewer`, `security-reviewer`: no CRITICAL; the one HIGH (a proxy insert
racing an entry deletion ends in `23503`) concerns group 5 and is built into its design (D4 above).
Applied: a missing `slots` or `version` is `required` (an empty list still clears the slots; a
missing set no longer wipes them); a comparsa deleted between the check and the save of its slot
answers `400 slots[i].comparsaId unknown` instead of a 500; the slot diff is a pure, unit-tested
`SlotDiff`; `pickup_proxies.comparsa_id` has its own index for the catalogue veto and the key check;
`ListOrderStatusesAsync` projects two columns; field error codes are constants
(`DistributionFieldErrors`); `AdminDayAsync` says it never serves a FiringChief; the write guard has
its own file and the DTOs are `DistributionDtos.cs`; tests for a held day lock (503, nothing stored),
unknown days, required fields, the 200-slot cap and clearing the slots. The forced `UpdatedAt` touch
is covered by the slot version test under the hosts' fixed clock (it failed without it). Kept: no
rate limit on these Admin-only writes, as for the other Admin writes; `Created` points to the day's
route although it has no `GET` (the plan is read per edition).

### Review notes (group 5)

`csharp-reviewer`, `security-reviewer`, `silent-failure-hunter`: no CRITICAL; BR-12 confirmed (no
existence oracle, scope before the edition rule before validation). HIGHs fixed: the concurrent test of
two holders naming each other now uses two active holders with powder and asserts one proxy (it passed
trivially before); the proxy list no longer hides a proxy silently — a proxy whose entry vanished
meanwhile (cascade) is left out and logged, a missing comparsa name fails loudly instead of printing
blank; the registration answer no longer fails after the commit when the proxy is already gone (404).
Also applied: a removal racing the cascade answers `404 proxies.notFound` (was a 500); `RegisterAsync`
is split into a check (scope → edition rule → entry rules → license, the registry read last) and a
write; the busy cause is logged by SQL state; a linked arquebusier the registry no longer returns is
logged; a slot key failure with every comparsa present fails loudly; an unknown distribution type
throws; the proxy-absent reason is a constant; operation names in the logs; tests for the held proxy
lock (503), missing fields, identical answers for any entry outside the order, the candidates'
exclusion and per-day license, and the pinned lock key. Kept: rules read before the transaction may
let a just-changed entry through; the proxy then shows its problem on every read (D5). No pagination
on the Admin list (under a thousand entries per edition).

### Review notes (group 6)

`csharp-reviewer`, `security-reviewer`, `silent-failure-hunter`: no CRITICAL; one HIGH, fixed: a name
erased on a GDPR request printed blank on a list or a form — it now prints a translated placeholder
and the list logs the count. Also applied: the comparsa part of the form's file name is capped at 40
characters, so a 100-character comparsa name still gives a valid name (it was a permanent 500); a
holder with a pending license prints no license type (spec: a type the holder lacks is blank); a
proxy whose entries do not match its order or comparsa fails loudly, a missing entry is logged, a
missing comparsa throws with its id; a logo the storage cannot serve is logged before the 503; the
audit data counts the proxies left out of a list; one `LiveAsync`; tests for lists without
non-validated orders or the other type's proxies, a form without a planned day, a closed edition
(the FiringChief still prints) and a draft (404), the erased placeholder, the long comparsa name,
and a Valencian golden form. Kept: no shared snapshot across modules (above); the audit failure
catch stays as broad as the exports' (it fails closed, logs at Error with the comparsa); an unknown
culture falls back to Spanish; the document name and version stay in the footer.

### Review notes (group 8)

`react-reviewer` and `a11y-architect`, no CRITICAL. Fixed: the Federation logo section offers a retry
when it cannot load and warns that it may be outdated when the refresh after a change fails (both
reviewers); a download clicked while another runs is announced as still on its way, a format
without a URL is not offered, and a single download's own label carries its full accessible name
(e.g. "Print form for …") instead of a composed one; `proxies.notFound` and `distribution.notFound`
read as "no longer exists"; `TimeInput` and `DateInput` empty a partly typed value when cleared,
and `TimeInput` names its clear button from a `clearSubject` with a translated pattern that starts
with the visible text (WCAG 2.5.3); the logo's loading text is a status; the Federation logo URL
helper sits in `logos.ts`; a FiringChief's comparsas page does not ask for the Federation settings.
Kept: `TimeInput` and `DateInput` stay separate composites (their shared partial-input handling is
small); `DateInput` gets no per-field clear name until a form shows several clearable dates; the
crop help's "on the right" wording is existing copy outside this change.

### Review notes (group 9)

`react-reviewer` (one HIGH) and `a11y-architect` (three HIGH), no CRITICAL. Fixed: deleting a day or
removing a proxy refreshes the page when the answer says it changed, and treats one already gone as
done; the stale answers are one set in `problems.ts`, and the refresh also reads the proxy
candidates again and resolves with the plan as it is now; after a conflict the day and slots
panels show the server's values (the slot rows come from the form, so labels stay with their
values); the panels stay mounted with hidden triggers when the edition stops being in progress, so
an open panel still says why; people who cannot be the proxy are also listed, with the reason, in
the field's description (browsers skip disabled options), and an unknown reason reads "does not
meet the conditions"; per-row names, dialog titles and the form's name carry the type, as a holder
may have a powder and a weapons proxy; the candidates' loading, failure (with retry) and "nobody"
states are announced; nested "Slots" and "List" regions became headings; the page says when the
edition is a draft or closed, shows a loading status, and puts the outdated-data warning under the
header; the proxy count is announced after filtering, focus falls back to the list when "Add proxy"
is not shown, the comparsas' load failure is shown, the address's filter is used before the
comparsas load, and inactive comparsas are listed for Admins too (their orders and proxies stay in
the edition). Tests added for these paths. Kept: every slot field is marked optional (the form's
note says fields are required unless marked); a day deleted while its slots panel is open closes
that panel with the section.

### Verification (task 10.2)

PASS on 2026-10-03:

- Build, types and lint: `dotnet build` (0 warnings) and `dotnet format --verify-no-changes`;
  `tsc -b`, ESLint (0 warnings), Prettier, `check-i18n`.
- Tests: backend 2,074 passed (then the 3 added below); frontend 2,210 passed. Line coverage:
  backend 96.1 % overall (`Distribution` 94.3 %, `Exports.Contracts` 93.3 %, `Exports` 99.2 %,
  `FederationCatalog` 97.2 %, `ComparsaOrders` 96.7 %); frontend `features/distribution` about
  90 %, the logo sections 90 % or more, `DownloadButtons` 100 %.
- E2E on the compose stack: the full suite passed twice in a row (276 passed, 14 conditional skips),
  `distribution.spec.ts` on desktop and at 360 px (axe, no sideways scrolling, bottom sheet) and the
  serial `distribution-planning.spec.ts`. The seed adds 2 days and 2 proxies once, nothing the
  second time.
- Security grep: every new route is under the signed-in `/api` group, with the Admin policy where
  the spec says; FiringChief scope is enforced in the handlers and pinned by
  `ComparsaScopeGuardTests` (proxy candidates added); logs carry ids only; audit data carries no
  names or DNI/NIE; file names carry neither; `no-store` comes from the host's headers (tested per
  document); no image file is added by the change.
- Diff review: no debug output, TODOs or stray files; `.claude/settings.local.json` stays out.

`e2e-runner` (no CRITICAL). Fixed: the FiringChief spec removes its proxy before and after the test,
so a failed run never leaves one; the serial spec restores each part independently (the order
first) from the seed's fixed values, waits for the crop preview, checks a FiringChief gets 403 on the
logo and reloads before checking a slot; axe also runs on the proxy panel, the confirmation and the
crop dialog; the bottom sheet at 360 px, a closed edition read-only, a pending license not eligible.
The shared `axeViolations` fixture now waits for finite animations to end, as WebKit measured a
dialog still fading in (an intermittent failure of `comparsa-logos.spec.ts`). Observed once each
under the parallel run, not reproducible alone (3 repeats): a Firefox `networkidle` timeout in
`select.spec.ts` and `insights.spec.ts` (existing specs). Follow-ups: list contents (numbering, proxy
columns) end to end, the weapons day and the other languages in E2E.

`pr-test-analyzer` (no CRITICAL). Fixed with tests: a table without rows renders with its headings
in Excel and PDF, and the endpoint returns an empty list; the weapons list through the endpoint
(rented model, file name, audit); the form with a stored Federation logo has exactly one image and
none without it. Follow-ups (MEDIUM/LOW): the license reference date per type at registration and
the expiry-on-the-day boundary through the endpoint; absence rules per type, lifted by a removal,
and their precedence under concurrency; numbering across comparsas through the endpoint; fuller
audit and file-name assertions, and no audit for refused forms; draft-edition scope on the proxy
list, candidates and removal; concurrent slot saves; lost-race branches (`DeletingCatalogDirectory`
style); logo processing details for the Federation logo; edition deletion with a proxy only; the
seed stores no Federation logo; a 401 theory for the distribution routes.

## Open Questions

- Q-43 (traceability 1 and 2): the powder list leaves both columns blank; their meaning matters only
  for UC-21.
- The Federation may prefer a different layout for the lists or the form; a new definition version
  changes only the builder and its golden files.
