# Design

## Context

See `proposal.md` for the motivation, and `specs/festival-editions/spec.md` and
`specs/arquebusier-registry/spec.md` for the behaviour. The facts below come from the current code
and shape the approach.

- **Module anatomy is fixed** (`backend/src/Modules/README.md`, ADR-0001):
  - a module has an implementation project and a `.Contracts` project, and implementation types
    are `internal`;
  - its `IModule` declares a `MigrationOrder`: audit runs first, identity is 10, the catalog 20
    and the registry 30;
  - `AddModuleDbContext` gives the module its schema and its own migrations history;
  - architecture tests allow a module to reference only SharedKernel and other modules'
    Contracts;
  - cross-schema foreign keys only point from a later module to an earlier one. They are added
    with `migrationBuilder.Sql` and `ON DELETE NO ACTION`.
- **Catalogue usage.** Deleting a weapon model goes through `ICatalogUsage`
  (`IsWeaponModelInUseAsync`). The catalog collects every registration and locks the row
  `FOR UPDATE` before asking. The interface's doc already names "edition availability".
- **The catalog contract lacks one field.** `ICatalogDirectory.FindWeaponModelsAsync` returns
  `WeaponModelSummary(Id, Kind, Side, Handedness, Size, Label, Active)`, with no `Rentable`.
- **Registry writes share a guard.** Every registry write goes through `RegistryWriteGuard.RunAsync`
  and opens its transaction with `db.BeginWriteAsync` (`lock_timeout` 5 s). The writes are
  register, update, transfer and delete in `ArquebusierAdministration`, add, update and remove in
  `OwnedWeaponAdministration`, upload and remove in `ArquebusierPhotoAdministration`, and
  `ArquebusierImporter`. Lock timeouts become `registry.busy` (503). Outcomes map to problems in
  one place, `RegistryProblems.From`.
- **Patterns already in use:**
  - concurrency: `xmin` row versions, `uint Version` in responses, and `uint? Version` in requests
    with `*.modified` (409) on a mismatch;
  - audit: `IAuditTrail.Record(db, AuditRecord)` before `SaveChangesAsync`, with PascalCase
    actions such as `ComparsaUpdated`;
  - dates: `InputFields.RequiredDate/OptionalDate` (`yyyy-MM-dd` strings), and
    `FederationCalendar.Today(TimeProvider)` for today in Europe/Madrid;
  - JSON uses `NumberHandling.Strict`;
  - there is no `decimal` or money anywhere yet.
- **Frontend:**
  - an `edition` status mapping already exists in `components/app/status.ts` and
    `docs/design/status.md`, with tone, icon and labels in three locales. It has the five statuses
    of the earlier data model. No screen uses it yet, so it can be replaced by the three statuses
    of this change;
  - the navigation is data (`app/navigation.ts`), and Admin routes sit under `RequireAdmin` in
    `app/routes.tsx`;
  - the start page is `features/compliance-insights/pages/DashboardPage.tsx`;
  - there is no money or decimal input composite. Numbers use `TextInput inputMode="numeric"`.

## Goals / Non-Goals

**Goals:**
- One new module that owns editions and exposes to #10, through its Contracts, exactly the facts
  orders need: the current edition, whether its orders are open, and the offered models.
- Keep two concerns apart: the edition lifecycle (`DRAFT`, `IN_PROGRESS`, `CLOSED`), and the
  orders flag (`ordersOpen`), which only the edition in progress can turn on.
- Enforce "at most one edition in progress" and the registry lock in the database transaction, so
  they hold under concurrent requests.
- Keep the registry module the only owner of registry writes. The lock lives and is checked there.

**Non-Goals:**
- A generic workflow or state-machine library. Three statuses with adjacent moves and one flag fit
  in two small checks.
- Caching the current edition or the lock state. Each check is a single-row read inside the write
  transaction.

## Decisions

### D1. A `FestivalEditions` module with an `editions` schema

The module has `PolvorApp.FestivalEditions` and `PolvorApp.FestivalEditions.Contracts`, with
`MigrationOrder = 40` and seeder `Order = 40`. It is registered in `Program.cs`, the solution, the
`Dockerfile` and `ModelDriftTests`.

Tables (snake_case):

| Table | Columns | Constraints |
|---|---|---|
| `festival_editions` | `id uuid`, `year int`, `festival_starts_on date`, `festival_ends_on date`, `orders_open_on`, `orders_close_on` (`date null`), `status text`, `orders_open boolean not null` (EF always writes it; no database default), `powder_per_kg`, `caps_box`, `weapon_rental`, `flask_rental` (`numeric(6,2) null`), `status_changed_at timestamptz null`, `xmin` | unique `year`; check `status` in `EnumCodes.All<EditionStatus>()`; check year 2000–2100; check prices 0–9999.99; check `NOT orders_open OR status = 'IN_PROGRESS'`; partial unique index `ux_festival_editions_in_progress` on `(status) WHERE status = 'IN_PROGRESS'` |
| `edition_weapon_models` | `edition_id`, `weapon_model_id` | PK on both; FK to `festival_editions` `ON DELETE CASCADE`; FK to `catalog.weapon_models` `ON DELETE NO ACTION` (raw SQL) |
| `calendar_milestones` | `id`, `edition_id`, `date date`, `title varchar(100)` | FK `ON DELETE CASCADE`; index `(edition_id, date)` |

`EditionPrices` is four nullable `decimal` columns on `FestivalEdition`, not a separate table,
because there is exactly one set per edition. It is not an EF owned type either: EF does not
create an owned dependent whose columns are all null, which is the normal state of a draft
(research, task 1.1). The API still groups the columns as a `prices` object. The date-order and within-year rules live in code (`EditionInput.Read`, in the
style of `RegistryInput`) and are repeated as check constraints only where they are simple
(`festival_ends_on >= festival_starts_on`).

*Alternative considered*: prices as a separate `edition_prices` table, as the data model sketches.
A 1:1 table adds a join and a second version to keep in step for no benefit.

### D2. Money as `decimal` / `numeric(6,2)` end to end

The C# type is `decimal`, the column is `numeric(6,2)`, and JSON carries a number. With
`NumberHandling.Strict`, System.Text.Json reads a JSON number into `decimal` from its text, so
`4.55` stays exact. The browser serialises two-decimal numbers with their shortest round-trip text
(`4.55`, `55`), which is exact as well. Validation rejects more than two decimals (`invalid`)
instead of rounding. Responses always carry two decimals (`55.00`).

*Alternative considered*: integer cents. This is exact too, but every DTO, form and audit entry
would then need a conversion, and a mistake would be off by 100. `decimal` is native in .NET,
PostgreSQL and the orval types (`number`).

### D3. Lifecycle and orders: two checks, one index

The earlier data model made the orders a stage of a five-status workflow (`ORDERS_OPEN`,
`CORRECTIONS_OPEN`, `LOCKED`). This change splits it in two (maintainer decision):
- the edition `status`: `DRAFT`, `IN_PROGRESS`, `CLOSED`;
- the `ordersOpen` flag.

A corrections window is the orders opened again.

`EditionStatusMoves.Check(from, to)` accepts only `|index(to) - index(from)| == 1` in the order
`DRAFT, IN_PROGRESS, CLOSED`. The status endpoint then:
1. opens a transaction (`lock_timeout` 5 s);
2. loads the edition and compares the version (`editions.modified`);
3. when moving from `DRAFT` to `IN_PROGRESS`, checks completeness and lists the missing field keys
   in the problem's `missing` extension (`editions.incomplete`);
4. when leaving `IN_PROGRESS` with `ordersOpen`, refuses with `editions.ordersOpen`. The Admin
   closes the orders first, so closing them is always an explicit, audited act;
5. saves, and audits `EditionStatusChanged { previous, current }`.

The orders endpoint takes `{ open, version }` in the same kind of transaction:
- it refuses an edition that is not `IN_PROGRESS` (`editions.notInProgress`);
- it returns unchanged, without auditing, when the flag already has that value;
- otherwise it audits `EditionOrdersOpened` or `EditionOrdersClosed`.

The check constraint `NOT orders_open OR status = 'IN_PROGRESS'` backs both rules in the database.

The partial unique index guarantees a single edition in progress under concurrency. A violation of
`ux_festival_editions_in_progress` maps to `409 editions.anotherInProgress`. Before saving, a plain
query finds the edition in progress, so the problem can carry an `inProgressYear` extension for the
message. The index stays the authority.

*Alternatives considered*:
- An advisory lock around the check. It is correct, but it adds a lock key and a convention that
  a unique index gives for free.
- Automatic moves by date: rejected by the maintainer.
- Closing the orders implicitly when the edition is closed or sent back to `DRAFT`. It saves a
  click, but hides a change that matters to FiringChiefs inside another action.

### D4. API

All routes are under `/api/editions`. Writes are in an Admin sub-group
(`RequireAuthorization(AuthorizationPolicies.Admin)`). Reads apply D6.

| Route | Success | Failures |
|---|---|---|
| `GET /api/editions` | `200 EditionRowResponse[]` (id, year, festival dates, status, ordersOpen, isCurrent), newest first | — |
| `GET /api/editions/current` | `200 CurrentEditionResponse { edition: EditionResponse \| null }` | — |
| `GET /api/editions/{id}` | `200 EditionResponse` | `404 editions.notFound` |
| `POST /api/editions` | `201 EditionResponse` (copied prices and models, D5) | `400 validation`, `409 editions.yearTaken` |
| `PUT /api/editions/{id}` | `200 EditionResponse`. Body: festival dates, two window dates, four prices, `version` | `400`, `404`, `409 editions.modified` |
| `POST /api/editions/{id}/status` | `200 EditionResponse`. Body: `status`, `version` | `400`, `404`, `409 editions.invalidTransition` / `anotherInProgress` / `incomplete` / `ordersOpen` / `modified` |
| `POST /api/editions/{id}/orders` | `200 EditionResponse`. Body: `open`, `version` | `404`, `409 editions.notInProgress` / `modified` |
| `PUT /api/editions/{id}/weapon-models` | `200 EditionResponse`. Body: `weaponModelIds[]` (at most 100) | `400 weaponModelIds: notFound \| notRentable`, `404` |
| `POST /api/editions/{id}/milestones` | `201 CalendarMilestoneResponse` | `400`, `404`, `409 editions.tooManyMilestones` |
| `PUT /api/editions/{id}/milestones/{milestoneId}` | `200` | `400`, `404` |
| `DELETE /api/editions/{id}/milestones/{milestoneId}` | `204` | `404` |
| `DELETE /api/editions/{id}` | `204` | `404`, `409 editions.notDraft` |

`EditionResponse` carries:
- `id`, `year`, `status`, `ordersOpen`, `version`;
- the festival and order window dates;
- `prices` (each nullable);
- `weaponModels[]` with `id`, `label`, `kind` and `offered`, which is false when the model is
  inactive or not rentable;
- `milestones[]` sorted by date, then title;
- `nextWindow { kind: OPENS | CLOSES, date } | null`.

`nextWindow` is the first order window date on or after today (Europe/Madrid), computed on the server,
so the detail page and the start-page card agree.

`/current` answers `200` with `edition: null` instead of `204`, so the orval hook has one response
type. Unchanged updates return `200` without saving or auditing. The OpenAPI document and the orval
client are regenerated.

### D5. Copy on creation

`POST` finds the edition with the greatest `year` lower than the new one. It copies its prices and
its `edition_weapon_models` rows whose model is still active and rentable, checked through
`ICatalogDirectory.FindWeaponModelsAsync`. The create form shows what will be copied, from
`GET /api/editions` plus the detail of that edition, so the server stays the single source of the
copy rule.

### D6. FiringChief visibility

Reads check `ICurrentUser.IsAdmin`. For FiringChiefs, queries add `status != 'DRAFT'`, and a draft
by id is `404 editions.notFound`, which is indistinguishable from an unknown id. No comparsa scope
applies: editions are Federation-wide.

### D7. Contracts for other modules

`PolvorApp.FestivalEditions.Contracts` exposes `IEditionDirectory` and its records:

- `Task<EditionSnapshot?> GetCurrentAsync(CancellationToken)`;
- `Task<EditionSnapshot?> FindAsync(Guid editionId, CancellationToken)`;
- `record EditionSnapshot(Guid Id, int Year, EditionStatus Status, bool OrdersOpen,
  DateOnly FestivalStartsOn, DateOnly FestivalEndsOn, IReadOnlyList<Guid> OfferedWeaponModelIds)`;
- `EditionStatus` (`DRAFT`, `IN_PROGRESS`, `CLOSED`), with `[JsonStringEnumMemberName]` codes.

`OrdersOpen` on the current edition is the single definition of BR-10's window rule: FiringChiefs
edit orders only while it is true. #10 reads it inside its own write transaction. If closing the
orders must wait for in-flight order writes, #10 adds a `FOR SHARE` read of the edition row to
this contract. `FestivalStartsOn` is the reference date #10 passes to
`IComplianceRules` (BR-04).

`FestivalEditionsCatalogUsage : ICatalogUsage` answers `IsWeaponModelInUseAsync` with
`edition_weapon_models`, and `IsComparsaInUseAsync` with `false`. It is registered with
`AddScoped<ICatalogUsage, …>()`.

The catalog contract gains one additive field: `WeaponModelSummary.Rentable`. Its existing callers
(the registry) ignore it. The `federation-catalog` spec does not change.

### D8. Registry lock in the registry module

The registry module owns the lock state and its enforcement, because the writes it guards are
registry writes. The state is a single row, `registry.registry_settings(id smallint primary key
check (id = 1), locked boolean not null default false, locked_changed_at timestamptz null)`,
inserted by its migration.

**Enforcement.** A new `RegistryLocks.EnsureWritableAsync(db, currentUser)` runs right after
`BeginWriteAsync` in every write a FiringChief can reach:
- register, update and delete;
- add, update and remove owned weapons;
- upload and remove photos.

For an Admin it returns immediately. For a FiringChief it runs
`SELECT locked FROM registry.registry_settings WHERE id = 1 FOR SHARE`. A locked registry returns
the new `RegistryOutcome.RegistryLocked`, which `RegistryProblems.From` maps to
`409 registry.locked`.

**Toggling.** `PUT /api/registry/lock { locked }` (Admin) takes `FOR UPDATE` on the same row. A
toggle therefore waits for in-flight FiringChief writes, and every FiringChief write that starts
after the lock commits sees it. This is the atomicity the spec requires. A lock that waits longer
than the 5 s timeout becomes `503 registry.busy`, as elsewhere.

**Photos.** Uploads process the image before their transaction. They also call a cheap unlocked
read first, so a locked registry refuses the upload before any image work. The in-transaction
check stays the authority, and an image stored before a late refusal is erased by the existing
rejected-upload path. Transfers and the import are Admin-only, so they skip the check by
construction.

**Endpoints.** `GET /api/registry/lock` is open to every signed-in user and returns
`RegistryLockResponse { locked, changedAt }`. It is a new `/registry` group, outside
`/arquebusiers`, so `RegistryScopeGuardTests` is unaffected. The toggle is audited as
`RegistryLocked` / `RegistryUnlocked` with entity type `Registry` and no comparsa. A toggle that
changes nothing is not audited.

**Review notes (group 4).**
- **Isolation.** The atomicity relies on PostgreSQL's default READ COMMITTED, which no
  transaction overrides. Under REPEATABLE READ, a blocked `FOR SHARE` would fail with 40001.
- **Lock convoy.** If a FiringChief write waits on an arquebusier row held by an Admin transfer, a
  toggle queues behind it, and new FiringChief writes queue behind the toggle. The stall is
  bounded by the 5 s lock timeout and ends in a retryable `registry.busy`. This is accepted at
  this scale.
- **Registration.** It refuses on the lock before its duplicate checks, so a locked registry
  never answers a FiringChief with `nationalIdTaken` or `federationIdTaken`, which reveal whether
  an ID already exists.

*Alternatives considered*:
- The lock as a column or row in the editions module, read by the registry through
  `IEditionDirectory`. The registry (order 30) would depend on a later module's Contracts for its
  core write path, and the lock is independent of editions (maintainer decision).
- An in-memory flag or a cached value. It would not be atomic with the writes, and would break
  with more than one API instance.

### D9. Audit actions

| Action | Entity | Data |
|---|---|---|
| `EditionCreated` | `FestivalEdition` | year, festival dates, copied prices, copied model ids, `copiedFrom` |
| `EditionUpdated` | `FestivalEdition` | `{ previous, current }` of the changed fields only |
| `EditionStatusChanged` | `FestivalEdition` | `{ previous, current }` |
| `EditionOrdersOpened` / `EditionOrdersClosed` | `FestivalEdition` | — |
| `EditionWeaponModelsChanged` | `FestivalEdition` | `added[]`, `removed[]` model ids |
| `CalendarMilestoneAdded` / `Updated` / `Removed` | `CalendarMilestone` | edition id, date and title (previous and current on update) |
| `EditionDeleted` | `FestivalEdition` | snapshot: year, status, dates, prices, model ids, milestone count |
| `RegistryLocked` / `RegistryUnlocked` | `Registry` | — |

None holds personal data. Each Administration class has its own private `Record` helper, as in the
catalog.

### D10. Frontend

**`features/festival-editions/`** holds:
- `pages/EditionListPage`, `pages/EditionCreatePage` and `pages/EditionDetailPage`;
- `components/EditionSections` (dates and order window, prices, models, milestones),
  `components/EditionActions` and `components/CurrentEditionCard`;
- `editionSchema.ts` (Zod, mirroring D1's rules), `problems.ts` and `queries.ts`.

**Routes:**
- `/editions` and `/editions/:id` for every signed-in user;
- `/editions/new` under `RequireAdmin`.

**Navigation:** `{ to: '/editions', labelKey: 'nav.editions', icon: CalendarDays }` after
Arquebusiers, for both roles.

**Edition actions.** One table maps each state to its primary action and its "More actions"
entries, with their label keys and confirmation texts:

| State | Primary | More actions |
|---|---|---|
| `DRAFT` | "Start edition" | "Delete edition" |
| `IN_PROGRESS`, orders closed | "Open orders" | "Close edition", "Back to preparation" |
| `IN_PROGRESS`, orders open | "Close orders" | "Close edition" and "Back to preparation", disabled with a hint to close the orders first |
| `CLOSED` | — | "Reopen edition" |

**Statuses.** The `edition` mapping in `components/app/status.ts` becomes:
- `DRAFT`: muted, "In preparation";
- `IN_PROGRESS`: success, "In progress";
- `CLOSED`: muted, "Closed".

A new `orders` mapping has:
- `OPEN`: success, "Orders open";
- `CLOSED`: info, Lock, "Orders closed".

`docs/design/status.md` is regenerated.

`editions.incomplete` lists `missing` in words inside the `ConfirmDialog`.
`editions.anotherInProgress` names `inProgressYear`. Success announces the new status through
`SaveNotice`.

**Offered models section.** The edit panel lists the active, rentable catalogue models as
`CheckboxField`s grouped by kind. A model that is offered but no longer rentable is shown checked,
with a warning `StatusBadge`, and can only be unchecked.

**Milestones section.** A `DataTable` (date, title) with add, edit and remove. Each opens an
`EditSheet` or a `ConfirmDialog`.

**`MoneyInput`** is a new composite in `components/app/`. It wraps `TextInput` with
`inputMode="decimal"` and a `€` suffix. It accepts the locale's decimal separator (`,` in es-ES and
ca-ES-valencia, `.` in en) and also `.`, and reports `invalid` beyond two decimals. Its `value` is
the typed text and the schema turns it into a number, so the user's text is never reformatted while
typing. It comes with a story and an axe test, and is documented in `docs/design/README.md`.

**Formatting.** Amounts are shown with `Intl.NumberFormat(locale, { style: 'currency', currency:
'EUR' })`, added to `useFormatters` as `currency`.

**Start page.** `DashboardPage` renders `CurrentEditionCard` first, outside the summary's
loading/failure states. The card uses `useGetCurrentEdition`, and has its own `LoadFailure` with
retry.

**Registry lock in `features/arquebusier-registry`:**
- a `useRegistryLock` query;
- an `AlertBanner` (info, `live={false}`) on the list and detail pages;
- an Admin action in the list's `PageHeader` with a `ConfirmDialog`.

For FiringChiefs while the registry is locked, the existing action components hide the register,
edit, status, delete, weapon and photo actions. A `registry.locked` problem from any write
invalidates the lock query, so the page switches to the locked state.

**i18n:**
- a new `editions` namespace (`list.*`, `create.*`, `detail.*`, `sections.*`, `actions.*`,
  `confirm.*`, `milestones.*`, `currentCard.*`, `errors.*`, `validation.*`), registered in
  `i18n/index.ts`;
- `common:nav.editions`;
- `registry:lock.*` and `registry:errors.locked`;
- `ui:moneyInput.*`.

Problem titles stay per HTTP status in `SharedResources*.resx`; the `editions.*` and
`registry.locked` codes are translated by the UI, as every other module's codes.

The three locales are checked by `npm run check-i18n` and `ResourceCompletenessTests`.

### D11. Seed and end-to-end isolation

`EditionSeeder` (order 40) computes its dates from `FederationCalendar.Today` at seed time (`t`):
- **Current edition.** `ordersOpenOn = t − 14 d`, `ordersCloseOn = t + 30 d`, and the festival
  from `t + 60 d` to `t + 63 d`. Its `year` is the festival's year. It is `IN_PROGRESS` with its
  orders open.
- **Past edition.** The previous year, `CLOSED`.
- **Draft.** The following year, `DRAFT`.

Every edition has fixed GUIDs and synthetic milestone titles, and existing rows are skipped. The
registry lock is seeded unlocked.

The end-to-end specs that change shared state are the registry lock and closing and reopening
the orders of the seeded current edition. They run in a separate Playwright project, `serial-state`. It depends on
the main projects, so it never overlaps with the FiringChief registry specs. Every one of its specs
restores the state it changed in `afterEach`. Edition create and delete flows use a far year
(2090–2099), picked per run, and delete their draft afterwards.

### Research notes (task 1.1)

Versions: EF Core 10.0.12, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, Playwright 1.63.0.

- **Single edition in progress.** EF Core cannot index a constant expression, so
  `ux_festival_editions_in_progress` is a unique index on `status`, filtered by
  `status = 'IN_PROGRESS'` (`HasIndex(e => e.Status).IsUnique().HasFilter(...)`). Every indexed row
  has the same value, so at most one row qualifies. No raw SQL is needed.
- **Prices.** They are plain nullable `decimal` properties with `HasPrecision(6, 2)`. An EF owned
  type would come back null whenever all its columns are null, which is the case for a draft
  without prices.
- **JSON.** `Utf8JsonReader` parses a JSON number into `decimal` from its digits, so it stays
  exact, and `NumberHandling.Strict` only forbids quoted numbers. The amount is rejected when
  `decimal.Scale` exceeds 2 after normalising trailing zeros.
- **Prices in the UI.** `Intl.NumberFormat(locale, { style: 'currency', currency: 'EUR' })` gives
  "55,00 €" in es-ES and ca-ES-valencia, and "€55.00" in en. The tests compare against
  `Intl` output instead of literal strings with a non-breaking space.
- **The `serial-state` project.** Playwright runs a project only after every project in its
  `dependencies` has passed, so its specs never overlap with the main projects.

## Risks / Trade-offs

- **A FiringChief's write slows down while an Admin toggles the lock.** The toggle waits at most
  5 s for in-flight writes, and writes wait for the toggle's commit. → Toggles are rare, and the
  wait is bounded by the existing `lock_timeout`.
- **A missed write path ignores the lock.** → A test class lists every registry write endpoint
  reachable by a FiringChief and asserts `409 registry.locked` for each. A route-table test, in the
  style of `RegistryScopeGuardTests`, fails for any new mutating `/arquebusiers` route that the
  class does not list.
- **Moving back to `DRAFT` hides an edition from FiringChiefs.** Today, nothing hangs on it. →
  #10 adds its own rule, such as "no back to preparation once orders exist", and the confirmation
  dialog says FiringChiefs lose sight of it.
- **There is no distinct corrections stage.** Reminders (#14) and FiringChiefs cannot tell a first
  opening from a reopening. → The maintainer accepted this. When reopening, the Admin updates
  `ordersCloseOn`, and can add a milestone for the corrections deadline.
- **Admin edits to prices after orders are validated change #11's billing.** → BR-10 allows Admin
  edits in any status. Every change is audited with the previous and new values, and the
  confirmation in the prices panel of a non-draft edition says that billing will use the new
  prices.
- **Seed dates are relative.** A database seeded months ago has a current edition whose order
  window has passed. → This is acceptable for development. The seed documentation says to reset the
  database for fresh dates.
- **`decimal` through JavaScript `number`.** → Two-decimal amounts at most 9999.99 are exactly
  representable as shortest round-trip text. Server validation rejects anything else, and a unit
  test round-trips every cent value of a sample range.

## Migration Plan

- Two additive migrations: `editions` (new schema and tables, then the cross-schema FK to
  `catalog.weapon_models`), and `registry` (`registry_settings` with its single unlocked row).
- They are applied by the existing `migrate` command, before the API starts. No data
  transformation is needed.
- Rollback: revert the deployment. The new tables are unused by the previous version and can stay,
  or be dropped by the migrations' `Down`.

## Verification (task 9.4)

PASS on 2026-10-03:

- Backend: `dotnet format --verify-no-changes` clean; 1438 tests pass; line coverage 95.9 % overall,
  97.7 % for `PolvorApp.FestivalEditions` and `PolvorApp.ArquebusierRegistry`; the lock code
  (`RegistryLockAdministration`, `RegistryLockEndpoints`, `RegistryLocks`, `EditionWriteGuard`) is
  fully covered.
- Frontend: ESLint, `tsc -b`, Prettier and `check-i18n` clean; 1858 tests pass; line coverage
  95.2 % overall, 84.9–92.8 % for `features/festival-editions`, 100 % for the registry lock hook,
  `money.ts` and `MoneyInput`.
- E2E: the full Playwright suite passes on the compose stack, the `serial-state` project included;
  seeding twice adds nothing the second time.
- Security grep: every edition, milestone and lock write route requires the Admin policy; the
  editions reads sit under the signed-in default of `/api`; audit entries carry years, statuses,
  prices and model ids, no personal data.
- Test review (`pr-test-analyzer`): no critical gaps. Added from its findings: the lock racing a
  registration (which moved the registration's audit entry inside its transaction, like every other
  write), an upload locked out after storing its image (refused, image erased), and client-side
  validation and a successful save of the dates panel. Follow-ups, low risk: Admin owned-weapon and
  photo writes while locked, more lock refusals in the UI (register form, weapons, photos), and the
  status moves' confirm-and-send in Vitest (covered by E2E today).
