# Design

## Context

See `proposal.md` for the motivation and `specs/federation-catalog/spec.md` for the behaviour.
The current state that shapes this design:

- `add-identity-access` (#3) left one seam for this change. `IFiringChiefAssignmentSource`
  (`IdentityAccess.Contracts`) is resolved by `ComparsaScope` once per request, and the identity
  module registers a deny-all default with `TryAddScoped`. `ComparsaAccess.Filter` already restricts
  `IQueryable`s to the accessible comparsas, and its guidance is to answer out-of-scope requests
  with 404.
- The principal is rebuilt on every request (`ValidationInterval = 0`), so `ICurrentUser.Role` is
  current. The scope is computed per request, so assignment changes take effect on the next request
  without further work.
- Module conventions (`backend/src/Modules/README.md`): one DbContext and one PostgreSQL schema per
  module, registered with `AddModuleDbContext(schema, migrationOrder)`. The audit trail is recorded
  with `IAuditTrail.Record(context, …)` before `SaveChangesAsync`. Implementation types are
  internal, and only `.Contracts` is public. Architecture tests enforce the reference rules.
- Problem responses carry a stable `code` extension that the UI translates. The helper that builds
  them (`Problems`) is internal to the identity module today.
- Users and arquebusiers are separate records. Only Admins and FiringChiefs are `User`s, and they
  are always invited. Arquebusiers never sign in. A FiringChief who also fires is an `Arquebusier`
  record as well (#5). Nothing in this change depends on that link.
- Frontend: feature folders under `src/features/`. `NAVIGATION` is data with `roles`, and
  `RequireAdmin` guards admin routes. The composites `DataTable`, `SelectInput`, `ConfirmDialog`,
  `StatusBadge` (with a status mapping per domain), `EmptyState`, `FormSection` and `PageHeader`
  already exist. The user detail page lives in `features/identity-access/pages/users/`.

## Goals / Non-Goals

**Goals:**
- Add a `FederationCatalog` module that owns comparsas, assignments and weapon models, and that
  makes the FiringChief scope real without changing the identity module's contracts beyond one
  small read interface.
- Build a reusable pattern for comparsa-owned endpoints and their authorisation tests. Later
  modules (#5, #10) will copy it.
- Keep the UI within the `components/app/` composites. One new composite is allowed, and only if
  the assignment picker needs it.

**Non-Goals:**
- No cross-schema foreign keys and no domain events between modules. Those are not needed yet.
- No caching of the scope beyond one request. At ~60 users one indexed query per request is
  negligible.
- No optimistic-concurrency tokens on catalogue edits. Instead, every change to an existing
  comparsa or weapon model locks its row first (D4), so concurrent edits serialise, the audited
  "previous" values are the ones actually replaced, and a record deleted meanwhile answers 404.

## Decisions

### D1. Module, projects and dependencies

`backend/src/Modules/FederationCatalog/`:
- `PolvorApp.FederationCatalog.Contracts` holds the public enums `Side`, `WeaponKind`, `Handedness`
  and `WeaponSize`. Later modules need them for arquebusiers and orders. Their codes come from each
  member's `JsonStringEnumMemberName` through a shared `SharedKernel.Codes.EnumCodes` helper
  (`ToCode`, `FromCode`, `Parse`, `All`), so JSON, database and validation use one source
  (implementation note: this replaces per-enum helpers like `UserRoleCodes`).
- `PolvorApp.FederationCatalog` holds `FederationCatalogModule` (`IModule`), `Persistence/`,
  `Comparsas/`, `Assignments/`, `WeaponModels/`, `Endpoints/` and `Seeding/`.

The implementation references `IdentityAccess.Contracts`, which ADR-0001 allows. The architecture
tests need no change. The host registers the module in `AddModules(...)` after `IdentityAccess`.

**`IFiringChiefAssignmentSource` implementation**: `FiringChiefAssignmentSource` queries
`firing_chief_assignments` by `user_id`. The module registers it with `services.Replace(...)`, so
it replaces the identity default whatever the registration order (implementation note: `Replace`
instead of a plain `AddScoped`, which would leave the default in `IEnumerable<>` resolutions). A host test asserts that the resolved implementation is the catalog
one. The default `NoFiringChiefAssignments` stays in place: identity tests that run without the
catalog then still deny by default.

*Alternative considered*: to put assignments in the identity module next to users. We rejected it
because `docs/mvp.md` places assignments in `federation-catalog`, and because the identity module
would then need to know about comparsas.

### D2. `IUserDirectory`, a new identity read contract

The catalog needs three things from the identity module: to validate that a user exists, is a
`FIRING_CHIEF` and is not `DEACTIVATED`; to show names, emails and statuses in the assignment lists;
and to offer candidates. It gets them through a new interface in `IdentityAccess.Contracts`:

```csharp
public interface IUserDirectory
{
    Task<UserSummary?> FindAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<UserSummary>> FindManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}
public sealed record UserSummary(Guid Id, string Name, string Email, UserRole Role, UserStatus Status);
```

`UserStatus` moves from the identity implementation to `Contracts`. The identity module implements
the interface with a read-only `AsNoTracking` query. The frontend gets the candidate FiringChiefs
from the existing `GET /api/users?role=FIRING_CHIEF` and drops the deactivated ones and those
already assigned. It gets the candidate comparsas from `GET /api/comparsas`.

*Alternative considered*: to replicate user data into the catalog schema. We rejected it: it means
duplication and synchronisation, and the directory call is cheap.

### D3. Data model and migration

Schema `catalog`, `MigrationOrder = 20` (after identity, 10). Every table uses snake_case.
Identifiers are UUIDv7, generated in the application with `Guid.CreateVersion7()`, as the audit
trail does.

| Table | Columns | Constraints |
|---|---|---|
| `comparsas` | `id` uuid PK, `name` varchar(100), `side` varchar(16), `active` bool, `created_at` timestamptz | generated `name_key` = `lower(name)` stored, unique index on it; check `side IN ('MOORISH','CHRISTIAN')` |
| `firing_chief_assignments` | `comparsa_id` uuid FK → `comparsas` `ON DELETE CASCADE`, `user_id` uuid, `assigned_at` timestamptz | PK (`comparsa_id`, `user_id`); index `user_id` (scope lookup); **no FK** to `identity.users` (no cross-schema FK, D1) |
| `weapon_models` | `id` uuid PK, `kind` varchar(16), `side`/`handedness`/`size` varchar(16) NULL, `rentable` bool, `label` varchar(100), `active` bool, `created_at` timestamptz | generated `label_key` = `lower(label)` stored, unique index on it; partial unique index (`kind`, `side`, `handedness`, `size`) `WHERE kind <> 'PISTOL'`; check `NOT (kind = 'PISTOL' AND rentable)`; check that `kind = 'PISTOL' OR (side, handedness, size all NOT NULL)`; check constraints on the enum values |

Enums are stored as their API codes (`MOORISH`, `TRABUCO` and so on) through EF value converters.
The rows then stay readable, and the codes stay stable if C# names change. The API validates the
rules first. The database constraints are the backstop for races, as with the unique email in #3:
a `UniqueViolation` becomes a `409` and a `CheckViolation` becomes a `400`.

Names and labels are trimmed and normalised to Unicode NFC on the server (a decomposed "é" would
otherwise bypass uniqueness). Inner whitespace is kept. `CHECK (btrim(...) <> '')` backs up the
non-blank rule, and the generated key columns are `text NOT NULL` (`lower()` can lengthen a value).
`lower()` folds accented capitals (Ñ, É) only with a UTF-8 ctype, so the database must not use
`C`/`POSIX` (documented in `docs/development.md`; tests cover accented capitals). Case-insensitive uniqueness
uses `lower()` rather than `citext`, so no extension is needed.

**API check (task 1.1, Context7 on EF Core 10.0.12 / Npgsql EF 10.0.3; `gh` is not installed on
the dev machine, so the real-world search was skipped):**
- *Expression indexes are not supported* by the Npgsql provider (the `IndexExpression` annotation
  is "currently unsupported and will be ignored"; only `IsTsVectorExpressionIndex` exists). A raw
  `migrationBuilder.Sql` index would be invisible to the model snapshot. **Workaround**: stored
  generated columns `name_key` / `label_key` =
  `HasComputedColumnSql("lower(name)", stored: true)`, with an ordinary unique index on them. This
  is EF-native, appears in the snapshot, and the key columns are never written by the application.
- *Filtered unique indexes*: `HasIndex(...).IsUnique().HasFilter("kind <> 'PISTOL'")` is supported.
- *Check constraints*: `ToTable(t => t.HasCheckConstraint(name, sql))` is supported. Constraint names
  are explicit (`ck_weapon_models_pistol_not_rentable`, …) so exceptions can be mapped.
- *Enums as codes*: `HasConversion(toCode, parse)` value converters, standard EF.
- *Exceptions*: `DbUpdateException` whose `InnerException` is `Npgsql.PostgresException` with
  `SqlState` `UniqueViolation` (23505) or `CheckViolation` (23514). `ConstraintName` tells the label
  index apart from the combination index (`weaponModels.labelTaken` vs `combinationTaken`), as
  `UserAdministration` already does for the email.

Removing an assignment deletes its row, and the audit trail keeps the history. Deleting a
comparsa or model deletes its row (D10), and the cascade removes the comparsa's assignments. The migration is
generated with the design-time factory pattern of `AuditPrivacy`, and the audit table is excluded
from it.

### D4. Rules and outcomes

Services (`ComparsaAdministration`, `AssignmentAdministration`, `WeaponModelAdministration`) return
an outcome enum, following `UserAdministration`. The endpoints map each outcome to a result. Input
validation (`CatalogInput`) collects field errors into `400 validation` with `errors{field: reason}`.

| Situation | Response | `code` |
|---|---|---|
| comparsa name taken | 409 | `comparsas.nameTaken` |
| comparsa not found / out of scope | 404 | `comparsas.notFound` |
| user not found | 404 | `assignments.userNotFound` |
| user not `FIRING_CHIEF` | 409 | `assignments.notFiringChief` |
| user `DEACTIVATED` | 409 | `assignments.userDeactivated` |
| comparsa inactive (new assignment) | 409 | `assignments.comparsaInactive` |
| model label taken | 409 | `weaponModels.labelTaken` |
| model combination taken | 409 | `weaponModels.combinationTaken` |
| model not found | 404 | `weaponModels.notFound` |
| comparsa referenced by other records | 409 | `comparsas.inUse` |
| model referenced by other records | 409 | `weaponModels.inUse` |
| invalid fields (incl. rentable pistol, missing attributes) | 400 | `validation` (`errors.rentable = "pistolNotRentable"`, `errors.handedness = "required"`, …) |

**Shared problem helper**: the generic part of the identity `Problems` class (`Problem`,
`Conflict`, `Invalid`) moves to `PolvorApp.SharedKernel/Http/ProblemResults`. The shared kernel
already depends on ASP.NET Core for `IModule`. Each module keeps its own code constants. This is a
refactor with no behaviour change, covered by the existing identity tests.

**Concurrency of assignments**: the assign operation loads the user through `IUserDirectory`
**before** opening the transaction (so no lock is held while waiting on the identity connection),
then reads the comparsa with `SELECT … FOR SHARE` inside an explicit transaction (so a concurrent
deactivation or deletion waits, and "inactive comparsas get no new assignments" holds) and inserts with `ON CONFLICT DO NOTHING` semantics. In
practice it catches `UniqueViolation` on the PK, reports `Done` and writes no audit entry. A race
with a concurrent role change or deactivation is acceptable: scope only applies while the role is
`FIRING_CHIEF`, and a deactivated user cannot sign in.

**Existing assignments**: assigning an assignment that already exists succeeds (204, no audit)
before any eligibility check, even if the comparsa is now inactive or the user now deactivated or
an Admin; only new assignments must be eligible (spec: "Repeated assignment").

**Row locks** (`CatalogLocks`, which refuses to run outside a transaction): editing, deactivating
and reactivating an existing comparsa or model take `SELECT … FOR NO KEY UPDATE` first (it
serialises changes and conflicts with the assigner's `FOR SHARE`, but does not block future
foreign-key inserts that reference the row); deleting takes `SELECT … FOR UPDATE`. All run in an
explicit transaction, with
`SET LOCAL lock_timeout = '5s'` so a stuck lock fails the request (as `UserLock` does in identity).
Lists are sorted in memory with a Spanish comparer (`CatalogOrder`), so "sorted by name" does not
depend on the database collation. Names and labels reject control, format (zero-width, bidi),
private-use and unassigned characters and line breaks (`invalid`).

**No-op detection**: an update whose values are all unchanged returns `Done` without recording an
audit entry, as `UserAdministration.UpdateAsync` does. The same holds when deactivating something
already inactive, reactivating something already active, repeating an assignment or removing an
assignment that does not exist.

### D5. API endpoints

All endpoints are under `/api`, authenticated by default (fallback policy), and take the
anti-forgery token on writes. Every operation has an OpenAPI name, a summary and its problem
responses.

| Method | Path | Access | Notes |
|---|---|---|---|
| GET | `/comparsas` | signed in, **scoped** | `?side=&includeInactive=`; returns `[{id,name,side,active}]`; sorted by name; `ComparsaAccess.Filter` |
| GET | `/comparsas/{id}` | signed in, **scoped** | 404 when out of scope |
| POST | `/comparsas` | Admin | `{name, side}` → 201 |
| PUT | `/comparsas/{id}` | Admin | `{name, side}` |
| POST | `/comparsas/{id}/deactivate` · `/reactivate` | Admin | |
| DELETE | `/comparsas/{id}` | Admin | 204; 409 `comparsas.inUse`; removes assignments (D10) |
| GET | `/comparsas/{id}/firing-chiefs` | Admin | `[{userId,name,email,status}]` from `IUserDirectory` |
| PUT | `/comparsas/{id}/firing-chiefs/{userId}` | Admin | assign, idempotent → 204 |
| DELETE | `/comparsas/{id}/firing-chiefs/{userId}` | Admin | unassign, idempotent → 204 |
| GET | `/firing-chiefs/{userId}/comparsas` | Admin | `[{id,name,side,active}]` of that user's assignments; 404 if the user does not exist |
| GET | `/weapon-models` | signed in | `?kind=&includeInactive=`; sorted by label |
| GET | `/weapon-models/{id}` | signed in | |
| POST | `/weapon-models` | Admin | `{kind,side?,handedness?,size?,rentable,label}` → 201; `rentable` required (an edit replaces the whole model, so a missing flag must not silently flip it); on a pistol an empty attribute counts as absent |
| PUT | `/weapon-models/{id}` | Admin | same body |
| POST | `/weapon-models/{id}/deactivate` · `/reactivate` | Admin | |
| DELETE | `/weapon-models/{id}` | Admin | 204; 409 `weaponModels.inUse` (D10) |

Enum fields arrive as text, so an invalid value is reported by field name, as `InviteUserRequest`
does. They are returned as string enums in the OpenAPI document, so orval generates union types.
Both assignment directions share the `/comparsas/{id}/firing-chiefs/{userId}` write endpoints. The
user page reads through `/firing-chiefs/{userId}/comparsas`. There is no route under `/users`,
which belongs to the identity module.

**Scope guard test**: a new integration test enumerates every endpoint whose route template
contains `/comparsas/{id}` from `EndpointDataSource`. It signs in as a FiringChief without
assignments and asserts that each one answers `403` or `404`, never `2xx`. This is the architecture
guard that D7 of #3 announced for the first comparsa-owned endpoint. Later modules get it for free.

### D6. Audit entries

Recorded with `IAuditTrail.Record` in the same unit of work (spec "Catalogue changes are audited"):

| Action | EntityType | EntityId | ComparsaId | Data |
|---|---|---|---|---|
| `ComparsaCreated` | `Comparsa` | id | id | `{name, side}` |
| `ComparsaUpdated` | `Comparsa` | id | id | `{previous:{name,side}, current:{…}}` |
| `ComparsaDeactivated` / `ComparsaReactivated` | `Comparsa` | id | id | — |
| `ComparsaDeleted` | `Comparsa` | id | id | `{name, side, active, unassignedUserIds}` |
| `FiringChiefAssigned` / `FiringChiefUnassigned` | `FiringChiefAssignment` | userId | comparsa id | `{userId}` |
| `WeaponModelCreated` | `WeaponModel` | id | — | all fields |
| `WeaponModelUpdated` | `WeaponModel` | id | — | `{previous, current}` with the editable fields only (no `active`) |
| `WeaponModelDeactivated` / `WeaponModelReactivated` | `WeaponModel` | id | — | — |
| `WeaponModelDeleted` | `WeaponModel` | id | — | all fields (snapshot) |

A deletion records one entry with a snapshot, because the row is gone afterwards. The assignments
removed with a comparsa are listed in that entry, instead of one `FiringChiefUnassigned` per user.

The data holds no personal data beyond user ids. Names and emails of users are not copied into
audit data.

### D7. Synthetic seed

`CatalogSeeder` (`IDataSeeder`, Order 20, after `IdentitySeeder` 10) inserts rows with fixed
identifiers and skips those that exist:
- 4 fictional comparsas: "Comparsa Sintética Norte" (`CHRISTIAN`), "Comparsa Sintética Sur"
  (`MOORISH`), "Comparsa Sintética Este" (`CHRISTIAN`) and "Comparsa Sintética Oeste" (`MOORISH`,
  inactive);
- 3 assignments: Jefe Uno → Norte and Sur, Jefa Dos → Norte. The fixed user ids come from
  `IdentitySeeder.Users`, shared through a constants class in the seeder's module (the ids are
  duplicated with a test asserting they match, because the catalog cannot reference the identity
  implementation);
- 9 weapon models: trabuco and arcabuz in `RIGHT`/`LEFT` × `NORMAL`/`SMALL` (8 rentable, one of
  them inactive), plus "PISTOLA" (not rentable, no attributes).

The same non-local guard as the identity seeder applies: outside Development and Testing, it
refuses a database that holds non-synthetic comparsas. E2E tests rely on these rows.

### D8. Frontend

Feature folder `src/features/federation-catalog/`:

| Route | Page | Access |
|---|---|---|
| `/comparsas` | `ComparsasPage`: `DataTable` (name, translated side, `StatusBadge` active/inactive); filters side and "include inactive"; "New comparsa" for Admins; `EmptyState` for a FiringChief without assignments | signed in |
| `/comparsas/new` | `ComparsaFormPage` (name, side) | Admin (`RequireAdmin`) |
| `/comparsas/:id` | `ComparsaDetailPage`: read-only summary for FiringChiefs; for Admins, the edit form, deactivate/reactivate and delete with `ConfirmDialog` (the delete confirmation states how many FiringChiefs lose access; after a delete the page returns to the list), and the **FiringChiefs** section (`AssignmentList`) | signed in; 404 → `NotFoundPage` |
| `/weapon-models` | `WeaponModelsPage`: `DataTable` (label, translated kind/side/handedness/size or "—", rentable, status); filters kind and "include inactive" | Admin |
| `/weapon-models/new`, `/weapon-models/:id` | `WeaponModelFormPage`: when kind is `PISTOL`, rentable is disabled and forced off, and side/handedness/size are optional; otherwise they are required (Zod schema mirrors the server); deactivate/reactivate and delete with `ConfirmDialog` on an existing model | Admin |

A `409 inUse` on delete is shown in the dialog (`ConfirmFailure`) with the translated reason and a
hint to deactivate instead.

- **Assignments from the user page**: `features/federation-catalog/components/UserComparsasSection`
  is rendered by the identity `UserDetailPage` when the user's role is `FIRING_CHIEF`. It is also
  rendered for an Admin who still has assignments, with an `AlertBanner` saying they have no
  effect. This is the one cross-feature import (identity → catalog). ESLint only restricts
  imports of `components/ui` primitives, so the import is allowed. We keep it to this single
  component so the coupling stays visible.
- **`AssignmentList`**: a shared inner component with the list, an add control and remove buttons,
  used by both sections. Adding uses the existing `SelectInput` composite fed with the eligible
  candidates (spec "Candidates exclude ineligible users and comparsas") and a `Button`. We do not
  need a searchable combobox: there are about 20 comparsas and about 60 users. Removing is confirmed
  with `ConfirmDialog`. Server `code`s map to `catalog:errors.<code>`. After a rejection both lists
  are refetched.
- **Queries**: orval-generated hooks. Mutations invalidate `comparsas`,
  `comparsas/{id}/firing-chiefs` and `firing-chiefs/{userId}/comparsas`.
- **Navigation**: `{ to: '/comparsas', labelKey: 'nav.comparsas', icon: Flag }` for every role, and
  `{ to: '/weapon-models', labelKey: 'nav.weaponModels', icon: Crosshair, roles: ['ADMIN'] }`. The
  icons are checked against lucide at apply time.
- **Status mapping**: new domain `catalog` in `components/app/status.ts` with `ACTIVE` (success) and
  `INACTIVE` (neutral), mirrored in `docs/design/status.md`. Its labels live in the `ui` namespace
  (`status.catalog.*`) like every other status, not in `catalog` (implementation note).

**i18n**: new namespace `catalog` in `es-ES`, `ca-ES-valencia` and `en`:
- `comparsas.*` (list, filters, form, detail, empty state, actions, confirmations, including the
  delete confirmation with the count of FiringChiefs losing access, pluralised);
- `firingChiefs.*` (section, add, remove, confirm, candidates empty, admin-no-effect notice);
- `userComparsas.*`;
- `weaponModels.*` (list, filters, form, pistol hint, delete confirmation);
- enums `side.MOORISH|CHRISTIAN`, `kind.TRABUCO|ARCABUZ|PISTOL`, `handedness.RIGHT|LEFT`,
  `size.NORMAL|SMALL`;
- `errors.<code>` for every D4 code and validation reason.

In `common` we add `nav.comparsas` and `nav.weaponModels`. The typed-keys declaration is extended.
Valencian texts are flagged for native review, as in #2 and #3. No backend `.resx` is needed: there
are no emails, and problem titles are already localised by the platform.

### D9. Security and GDPR

- **BR-12 / SEC-03**: the scope is enforced on the server through `IComparsaScope` on both comparsa
  reads. Out-of-scope requests answer 404. Assignment and management endpoints are Admin-only
  (`AuthorizationPolicies.Admin`). The D5 guard test covers every future `/comparsas/{id}` route.
- **SEC-05**: every write is audited in the same transaction (D6), and deletions carry a snapshot.
  No-ops are not audited.
- **Personal data**: comparsas and weapon models are not personal data. Assignments link a user id
  to a comparsa. Names and emails come from the identity module at read time, only for Admins, and
  are never logged or copied into audit data (NFR-12). The comparsa list that FiringChiefs see
  contains no user data.
- **SEC-11**: the seed uses fictional comparsa names and synthetic users only. No real comparsa list
  is committed (maintainer decision), although comparsa names are public organisations.
- **Input**: all enums are validated as whitelisted codes (requests take them as text and parse
  them with `EnumCodes`; the JSON converter rejects integers), and lengths are bounded. EF
  parameterises every query; the only raw SQL is in the migration (check constraints, generated
  columns) and the row locks of D4/D10.

### D10. Deletion and usage checks

Deletion is a hard delete of the row. Its history lives in the audit trail (D6). Before deleting,
the service asks every registered usage check whether the record is in use. It does so inside the
same transaction:

```csharp
// PolvorApp.FederationCatalog.Contracts
public interface ICatalogUsage
{
    /// <summary>True when records of the implementing module reference the comparsa.</summary>
    Task<bool> IsComparsaInUseAsync(Guid comparsaId, CancellationToken ct);

    /// <summary>True when records of the implementing module reference the weapon model.</summary>
    Task<bool> IsWeaponModelInUseAsync(Guid weaponModelId, CancellationToken ct);
}
```

The catalog resolves `IEnumerable<ICatalogUsage>`. In this change none is registered, so an existing
record can always be deleted. The registry (#5), editions (#9) and orders (#10) register their own
implementations. They reference only `FederationCatalog.Contracts`, which ADR-0001 allows. Tests in
this change use a fake implementation to cover the `409 inUse` path. Assignments are not a "use":
they are catalog data, and the cascade removes them with the comparsa.

*Race*: a later module could insert a reference between the check and the delete. The later
changes close that race when they add the references, either with a database constraint across
the schemas or by locking the comparsa row (`SELECT … FOR SHARE`) when they insert. That decision
belongs to #5, where the first reference appears. Both deletes here (comparsa and weapon model) open
an explicit transaction and take `SELECT … FOR UPDATE` on the row **first**; only then do they run
the `ICatalogUsage` checks and, for a comparsa, read the assignment user ids for the audit
snapshot, so a concurrent assignment is either blocked or included in `unassignedUserIds`.

*Alternatives considered*:
- Soft delete only, as in the first draft. The maintainer wants typos and unused records to
  disappear.
- Blocking the deletion of a comparsa that has assignments. That forces removing each FiringChief
  first. The confirmation states the count instead.

### D11. Composite changes made while building the screens (implementation notes)

Small, backwards-compatible options in `components/app/`, each with tests (and stories where the
catalogue requires them):

- `DataTable`: `paginated={false}` (every row, no pagination controls) and `emptyText` for the
  short assignment lists.
- `FilterSelect` (new composite): a labelled select above a list, replacing the copy that lived in
  `UsersPage`, which now uses it too; `lib/search-filters.ts` holds the shared address helpers.
- `Form` and `FormField`: an optional second type parameter for what the resolver submits, so a
  select can hold `''` until chosen while the form submits a code or `null`.
- `ConfirmDialog`: `onConfirmed`, run after the dialog has closed following a successful
  confirmation, with focus left to it. Announcing the outcome inside `onConfirm` lost focus to
  `body` when the refreshed page removed the trigger (WCAG 2.4.3).

Frontend dependencies on identity-access (besides the `UserComparsasSection` rendered from the
user page): the session (`useSession`) to choose the Admin or FiringChief view, and the users list
(`useListUsers({ role: 'FIRING_CHIEF' })`) for the FiringChief candidates.

Copy for native review (Valencian, flagged in the pull request as in #2 and #3): "Comparses",
"Bàndol", "Trabuc", "Arcabús", "Dretà" / "Esquerrà", "Xicotet", "Grandària", "Llogable",
"Es pot llogar", "Afig", "Lleva", "Esborra", "en compte d'esborrar-la", and the demonstratives
"este/esta/estes".

### Follow-ups from the group 8 reviews (not blocking, app-wide)

These affect every screen, not only the catalogue, so they belong to a separate change rather than
this one:

- `DataTable` announces the result count when filters change (today only sort and page changes
  are announced).
- 44 px touch targets on coarse pointers (checkboxes, small buttons, selects); today they meet
  WCAG 2.5.8 (24 px) but not the 44 px goal.
- Real h2 headings for page sections: `FormSection` is a fieldset/legend, so heading navigation
  skips "FiringChiefs", "Actions" and "Comparsas".
- Axe in component tests with `wcag22aa` and `best-practice`, in the three languages and both
  themes; target size and contrast checked in Playwright at 375 px.
- A typed helper instead of `data?.data as T` casts on generated responses (the pattern predates
  this change), and typed `catalog:`/`identity:` message keys for `FormField` errors.
- Notices: placement after actions far from the top of the page, and clearing a stale "saved"
  notice on the next submission; a loading status on detail pages.

### Test review notes (group 9)

- The UI never shows a stale assignment list after a change made on the other page: queries use
  `staleTime: 0` and refetch on mount, so no cross-page invalidation test is needed.
- That an audit entry and its change commit or roll back together is covered by the generic audit
  tests of #3. The catalogue only records through `IAuditTrail` in the same unit of work (D6).
- The race between an assignment and a deactivation of the same user is accepted (D4), so it has no
  test. The uniqueness races (name, label, combination) and the assignment races have barrier tests.

## Risks / Trade-offs

- [Assignments survive a role change to Admin and reapply after a later demotion] → The user detail
  page shows them with a "no effect while Admin" notice, so the Admin sees what a demotion would
  restore. Every assignment change is audited.
- [No FK between assignments and `identity.users`: an assignment can point to an unknown user] →
  Users are never deleted (identity spec), and assignments are validated through `IUserDirectory`
  at write time. The list endpoints skip ids that the directory does not return. A test covers this.
- [The free kind/side combination allows a mistaken "TRABUCO MOORISH"] → This is a maintainer
  decision (Federation naming does not follow Q-07). Labels are shown as entered, and the Admin can
  edit or deactivate a wrong model. We revisit this when the Federation confirms the catalogue.
- [Free edits of models before they are referenced; later changes need restrictions] → #5, #9 and
  #10 add rules for referenced models (for example, the kind is immutable once an owned weapon uses
  it). This change documents that edits are unrestricted only for now.
- [Cross-feature import from identity to catalog in the frontend] → It is limited to one section
  component. If more cross-feature pieces appear, they move to a slot in `app/`.
- [The seeded user ids are duplicated in the catalog seeder] → A test asserts that they equal the
  identity seeder's ids.
- [A hard delete removes data that turns out to be needed] → Deletion needs a confirmation, is
  Admin-only and is blocked while other records reference the comparsa or model. The audit entry
  keeps a snapshot, so the Admin can recreate the record by hand if needed.
- [A later module forgets to register its usage check] → Each later change that references
  comparsas or models adds a delete-while-referenced test to its own task list. The #5 proposal
  must name the check.

## Migration Plan

1. Deploy: the `migrate` command applies the `catalog` migration after `identity`. It only adds
   tables, and nothing existing changes.
2. After deploy, an Admin creates the comparsas and weapon models and assigns FiringChiefs. Until
   then FiringChiefs keep an empty scope, as today.
3. Rollback: redeploy the previous image. The `catalog` schema is ignored by it, and it can be
   dropped manually if needed. No data in other schemas depends on it.

## Open Questions

- The exact Federation labels of the catalogue. Admins enter them, so they do not affect this
  change.
