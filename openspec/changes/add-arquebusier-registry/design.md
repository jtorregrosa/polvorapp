# Design

## Context

See `proposal.md` for the motivation and `specs/arquebusier-registry/spec.md` for the behaviour.
The current state that shapes this design:

- **Comparsa scope.** `IComparsaScope.GetAccessAsync` (`IdentityAccess.Contracts`) returns a
  per-request `ComparsaAccess`, which offers `IsAll`, `CanAccess(id)` and
  `Filter(query, x => x.ComparsaId)`. Since #4 it comes from the real FiringChief assignments.
  Out-of-scope data is answered with `404`.
- **Catalog data.** The federation catalog owns comparsas and weapon models in schema `catalog`
  (`MigrationOrder` 20). It exposes the coded enums `Side`, `WeaponKind`, `Handedness` and
  `WeaponSize`, and the veto contract `ICatalogUsage`. Its delete already maps a PostgreSQL
  `ForeignKeyViolation` to `inUse`, and logs a warning, because #4 design D10 left a cross-schema
  foreign key as an option for this change. The catalog has no read contract for other modules
  yet.
- **Module conventions** (`backend/src/Modules/README.md`): one DbContext and schema per module;
  `IAuditTrail.Record` before `SaveChangesAsync`; `ProblemResults` with stable `code`s; coded enums
  through `EnumCodes`; generated `lower()` key columns for case-insensitive uniqueness; and
  `ModelDriftTests`.
- **Platform.** `TimeProvider` is injected (catalog, audit). API responses already carry
  `Cache-Control: no-store`. Logs must not contain personal data (NFR-12).
- **Frontend.** The composites `DataTable` (client-side sorting and paging, no global filter),
  `FilterSelect`, `SelectInput`, `TextInput` (no `date` type), `CheckboxField`, `FormField`,
  `FormSection`, `ConfirmDialog`, `StatusBadge`, `EmptyState` and `AlertBanner` exist.
  `STATUS_MAP` already has `arquebusier` (ACTIVE, RESERVE) and `license` (VALID, EXPIRING,
  EXPIRED, PENDING), with labels in `ui:status.*`.

## Goals / Non-Goals

**Goals:**
- Add an `ArquebusierRegistry` module that owns arquebusiers and owned weapons, follows the
  scoping and audit patterns of #4, and closes the comparsa and weapon-model deletion race from
  #4 D10.
- Keep personal data out of URLs, logs and audit data, so BR-14 deletion and later GDPR erasure
  (#15) stay complete.
- Give the later changes stable seams: #6 photos, #7 warnings, #8 import (which reuses the same
  validation), and #10 entries, loans and anonymisation.

**Non-Goals:**
- No server-side search or pagination. A FiringChief sees about 85 rows and an Admin about 800,
  so one scoped list plus client-side filtering is enough (NFR-05).
- No domain events between modules.
- No `EXPIRING` license status: "expiring soon" is a compliance warning (#7).

## Decisions

### D1. Module, projects and dependencies

`backend/src/Modules/ArquebusierRegistry/`:
- `PolvorApp.ArquebusierRegistry.Contracts` holds the public coded enums `ArquebusierStatus`
  (`ACTIVE`, `RESERVE`), `Gender` (`MALE`, `FEMALE`, `UNSPECIFIED`) and `LicenseType` (`AE`,
  `A_PROF`), and `LicenseStatus` (`PENDING`, `VALID`, `EXPIRED`). Orders (#10) and compliance (#7)
  need them.
- `PolvorApp.ArquebusierRegistry` holds the following folders:
  - `ArquebusierRegistryModule`;
  - `Persistence/` (DbContext, migrations, locks);
  - `Arquebusiers/` (entity, `ArquebusierAdministration`, `RegistryInput`);
  - `NationalIds/` (validator);
  - `Licenses/` (value object, defaults, derived status);
  - `OwnedWeapons/`;
  - `Endpoints/`;
  - `Seeding/`;
  - `RegistryCatalogUsage`.

The implementation references `IdentityAccess.Contracts` (scope, current user) and
`FederationCatalog.Contracts` (enums, `ICatalogUsage`, the new `ICatalogDirectory` from D2), as
ADR-0001 allows. The host registers the module after `FederationCatalog`. The Dockerfile restore
layer and `PolvorApp.slnx` list both projects. `ModelDriftTests` covers the new context.

### D2. `ICatalogDirectory`, a new catalog read contract

The registry needs four things from the catalog:
- whether a comparsa exists and is active (when registering and transferring);
- the comparsa names for the list;
- whether a weapon model exists and is active;
- the model attributes, to show owned weapons.

It gets them through a read contract in `FederationCatalog.Contracts`, implemented by the catalog
with `AsNoTracking` queries:

```csharp
public interface ICatalogDirectory
{
    Task<ComparsaSummary?> FindComparsaAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ComparsaSummary>> FindComparsasAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<WeaponModelSummary?> FindWeaponModelAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<WeaponModelSummary>> FindWeaponModelsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
public sealed record ComparsaSummary(Guid Id, string Name, Side Side, bool Active);
public sealed record WeaponModelSummary(Guid Id, WeaponKind Kind, Side? Side, Handedness? Handedness,
    WeaponSize? Size, string Label, bool Active);
```

This mirrors `IUserDirectory` (#4 D2). The registry's API responses embed `comparsaName` and the
model summary, so the frontend renders one response without joining lists.

*Alternative considered*: let the frontend join ids with `/api/comparsas` and `/api/weapon-models`.
We rejected it because the server needs the same data to validate anyway, and exports (#12) will
need names server-side.

### D3. Data model and migration

Schema `registry`, `MigrationOrder = 30` (after catalog, 20). Tables use snake_case, and ids are
UUIDv7 from `Guid.CreateVersion7()`.

| Table | Columns | Constraints |
|---|---|---|
| `arquebusiers` | `id` uuid PK, `comparsa_id` uuid, `federation_id` int, `national_id` varchar(9), `first_name` varchar(100), `last_name` varchar(100), `birth_date` date, `email` varchar(254) NULL, `phone` varchar(20) NULL, `gender` varchar(16), `status` varchar(16), `training_completed_on` date NULL, `license_type` varchar(16) NULL, `license_pending` bool, `license_issued_on` date NULL, `license_expires_on` date NULL, `created_at` timestamptz, `xmin` (concurrency token) | unique `ix_arquebusiers_federation_id`, unique `ix_arquebusiers_national_id`; index `comparsa_id`; checks on the enum codes, `federation_id BETWEEN 1 AND 999999999`, `birth_date >= 1900-01-01`, phone and email formats (email lower-case and trimmed), `national_id ~ '^([0-9]{8}\|[XYZ][0-9]{7})[A-Z]$'`, names non-blank, trimmed and without control characters, and `ck_arquebusiers_license` (no license ⇒ all license columns empty and not pending; pending ⇒ type set, no dates; issued ⇒ type and both dates set, `expires_on > issued_on`); **FK `comparsa_id` → `catalog.comparsas(id)` `ON DELETE NO ACTION`** |
| `owned_weapons` | `id` uuid PK, `arquebusier_id` uuid FK → `arquebusiers` `ON DELETE CASCADE`, `weapon_model_id` uuid, `weapon_number` varchar(30), `ownership_guide_number` varchar(30), `created_at` timestamptz, `xmin` | unique `ix_owned_weapons_ownership_guide_number` on `ownership_guide_number` (stored trimmed and upper-cased; the check compares with `upper()` under `COLLATE "C"` so it does not depend on the database collation); weapon number non-blank and trimmed; index `arquebusier_id`; **FK `weapon_model_id` → `catalog.weapon_models(id)` `ON DELETE NO ACTION`** |

- The **license** is mapped to the `license_*` columns of the same row: at most one, with no
  history and no number. `license_type IS NULL` means no license.

  **API check (task 1.1, Context7 on EF Core 10.0.12 / Npgsql EF 10.0.3; `gh` is not installed on
  the dev machine, so the real-world search was skipped):**
  - *License mapping*: the license is **not** a complex type. Keys and indexes on complex types are
    EF 11 features, and an optional complex type whose members are all null is ambiguous with
    "not pending". Instead it is four plain nullable properties on `Arquebusier`
    (`LicenseType`, `LicensePending`, `LicenseIssuedOn`, `LicenseExpiresOn`), behind a `License?`
    value object that the entity builds and validates. The database check constraint enforces
    consistency.
  - *`xmin`*: a `uint` property configured with `.IsRowVersion()` maps to `xid`. The Npgsql
    convention names the column `xmin`, it is excluded from migrations as a system column, and a
    stale value raises `DbUpdateConcurrencyException`.
  - *`DateOnly`*: maps natively to `date` in Npgsql 10.
  - *Cross-schema FKs*: `migrationBuilder.Sql("ALTER TABLE registry.… ADD CONSTRAINT … REFERENCES
    catalog.…(id) ON DELETE NO ACTION")`; `Down` drops the tables, and the constraints with them.
    *Implementation note*: `NO ACTION`, not `RESTRICT`. A `RESTRICT` violation raises
    `restrict_violation` (23001), which the catalog does not map; `NO ACTION` raises
    `foreign_key_violation` (23503), already mapped to `inUse` (found by the database tests).
  - *Exceptions*: `PostgresException.ConstraintName` identifies unique (23505), check (23514) and
    foreign-key (23503) violations, as the catalog already does.
- **Cross-schema foreign keys** close the race that #4 D10 left open. Inserting a row that
  references a comparsa or model takes `FOR KEY SHARE` on the referenced row, and the catalog's
  delete takes `FOR UPDATE` on it. So either the delete waits and then fails with
  `ForeignKeyViolation` (already mapped to `409 inUse`), or the insert fails because the row is
  gone (mapped to `404` / `400 notFound`). EF cannot model a foreign key into another context, so
  the migration adds both constraints with `migrationBuilder.Sql`. They are invisible to the model
  snapshot, and an integration test asserts that they exist. `RegistryCatalogUsage` still answers
  `ICatalogUsage` (`EXISTS` queries), so the normal path returns `inUse` without relying on the
  exception, and the catalog's "referenced without usage check" warning only fires in a real
  race. The convention is documented in `backend/src/Modules/README.md`: a module may reference
  another module's table only by foreign key, never by query, and only in migration order.
- **Optimistic concurrency** uses PostgreSQL `xmin` as the row version (a `uint` with `IsRowVersion()`, see the
  task 1.1 note above). It is exposed as `version` in the detail responses and
  required on `PUT`. A mismatch raises `DbUpdateConcurrencyException`, which becomes
  `409 arquebusiers.modified` / `ownedWeapons.modified`.
- **Normalisation** before storage: text is trimmed and in NFC; `email` is lower-cased; `phone`
  collapses inner runs of spaces; `national_id` follows D4; `ownership_guide_number` is
  upper-cased with the invariant culture. Names reject line breaks, control, format, private-use
  and unassigned characters, through `SharedKernel.Validation.InputFields`, moved there from the catalog's `CatalogInput`
  (implementation note).
- **Today** is `DateOnly` in Europe/Madrid, from `TimeProvider` through a small
  `SharedKernel.Time.FederationCalendar.Today(TimeProvider)`. #7 and #9 reuse it. The IANA id
  works on Windows and Linux with .NET ICU.

### D4. National ID validation, shared by server and UI

The rule is a pure function on both sides:
1. Remove spaces (U+0020), tabs (U+0009) and hyphens (U+002D) only. Reject at once if any other
   character is outside ASCII `0-9A-Za-z`, so Cyrillic and Greek look-alikes, full-width digits,
   other Unicode spaces and dashes are rejected. Inputs longer than 64 characters are rejected
   before they are copied.
2. Upper-case the value (ASCII only).
3. Match `^[0-9]{8}[A-Z]$` (DNI) or `^[XYZ][0-9]{7}[A-Z]$` (NIE, with prefix X→0, Y→1, Z→2).
4. Check that the letter is `"TRWAGMYFPDXBNJZSQVHLCKE"[number % 23]`.

The reasons are `invalid` (format) and `checkLetter`. A DNI with fewer than 8 digits is invalid,
and the UI hint says to add leading zeros. The test vectors live in one synthetic file,
`contracts/test-vectors/national-ids.json` (input, normalised result or reason). Both the xUnit
`[Theory]` and the Vitest suite read it, so the two implementations cannot drift. #8 import reuses
the server function.

*Alternative considered*: only server validation. We rejected it because the spec requires instant
feedback and the rule is small and stable.

### D5. Rules, outcomes and problem codes

`ArquebusierAdministration` and `OwnedWeaponAdministration` return outcome enums, which the
endpoints map. This follows `UserAdministration` and the catalog services. `RegistryInput`
collects the field errors.

| Situation | Response | `code` |
|---|---|---|
| invalid fields | 400 | `validation`, `errors{field: reason}` with reasons `required`, `invalid`, `tooLong`, `checkLetter`, `future`, `tooOld`, `notAfterIssued`, `datesWhilePending`, `notFound` (weapon model) |
| arquebusier not found or out of scope | 404 | `arquebusiers.notFound` |
| comparsa not found or out of scope (register, transfer) | 404 | `arquebusiers.comparsaNotFound` |
| comparsa inactive (register, transfer target) | 409 | `arquebusiers.comparsaInactive` |
| transfer to the same comparsa | 409 | `arquebusiers.sameComparsa` |
| nationalId taken | 409 | `arquebusiers.nationalIdTaken` |
| federationId taken | 409 | `arquebusiers.federationIdTaken` |
| outdated version | 409 | `arquebusiers.modified` / `ownedWeapons.modified` |
| owner of an owned weapon not found or out of scope | 404 | `arquebusiers.notFound` |
| owned weapon not found under an owner in scope, or removed concurrently | 404 | `ownedWeapons.notFound` |
| inactive weapon model on add or model change | 409 | `ownedWeapons.modelInactive` |
| ownership guide taken | 409 | `ownedWeapons.guideTaken` |
| transfer by a FiringChief | 403 | (authorization policy) |

- **Uniqueness** is checked first with a query, so the common case gives a clear message. The
  unique indexes are the backstop for races: a `UniqueViolation` is mapped by `ConstraintName`
  (`ix_arquebusiers_national_id` and so on). Neither path returns anything about the other
  record.
- **Scope**: every query goes through `access.Filter(..., a => a.ComparsaId)`, and every row lock
  carries the scope in its `WHERE` clause, so an out-of-scope row is never locked and answers like
  an unknown id. A registration checks `access.CanAccess(comparsaId)` before calling the catalog.
  *Implementation note*: the request body is validated first (a `400` depends only on the body,
  never on the id), and a valid request on an out-of-scope id then gets `404`. Write routes must
  never consult stored data before the scoped load.
- **Unknown members** in a request body are rejected (`400`): an edit replaces every field, so a
  misspelt optional field would otherwise clear it silently (group 3 review).
- **Lock timeout** (`55P03`, another request held the row past 5 s) → `503 registry.busy`,
  retryable. Every rejection is logged at Warning with the operation, outcome and ids only.
- **Inactive comparsa race**: a registration racing with a deactivation of its comparsa may still
  succeed. That is harmless (deactivation keeps history), and the catalog's `FOR NO KEY UPDATE`
  lock does not block foreign-key inserts by design (#4 D4).
- **No-op**: a `PUT` whose normalised values equal the stored ones returns `200` without saving or
  auditing. `xmin` is unchanged, so the client keeps its version.
- **License defaults** (BR-03): when `issuedOn` is set and `expiresOn` is null, `expiresOn` becomes
  `issuedOn.AddYears(5)` (AE) or `AddYears(1)` (A_PROF). `AddYears` maps 29 February to
  28 February. The derived `licenseStatus` is computed on read from `FederationCalendar.Today`.

### D6. API endpoints

All endpoints are under `/api`, authenticated by default, with the anti-forgery token on writes,
an OpenAPI name, a summary and the problem responses. Personal data never goes in a URL: ids are
UUIDs, and search is client-side (D8).

| Method | Path | Access | Notes |
|---|---|---|---|
| GET | `/arquebusiers` | signed in, **scoped** | `?comparsaId=&status=`; rows `{id, firstName, lastName, nationalId, federationId, comparsaId, comparsaName, status, licenseStatus, licenseExpiresOn}`; sorted by last name then first name with a Spanish comparer |
| GET | `/arquebusiers/{id}` | signed in, scoped | full detail: the row fields, `birthDate`, `email`, `phone`, `gender`, `trainingCompletedOn`, `license {type, pending, issuedOn, expiresOn, status}` or null, `ownedWeapons [{id, model: WeaponModelSummary, weaponNumber, ownershipGuideNumber, version}]`, `comparsaActive`, `version` |
| POST | `/arquebusiers` | signed in, scoped | `{comparsaId, federationId, nationalId, firstName, lastName, birthDate, email?, phone?, gender, status?, trainingCompletedOn?, license?}` → 201 with the detail |
| PUT | `/arquebusiers/{id}` | signed in, scoped | the same body without `comparsaId`, plus `version` → 200 with the detail. A full replace: an omitted optional field is cleared |
| DELETE | `/arquebusiers/{id}` | signed in, scoped | 204 |
| POST | `/arquebusiers/{id}/transfer` | Admin | `{comparsaId}` → 204 |
| POST | `/arquebusiers/{id}/owned-weapons` | signed in, scoped | `{weaponModelId, weaponNumber, ownershipGuideNumber}` → 201 |
| PUT | `/arquebusiers/{id}/owned-weapons/{weaponId}` | signed in, scoped | the same body plus `version` → 200 |
| DELETE | `/arquebusiers/{id}/owned-weapons/{weaponId}` | signed in, scoped | 204 |

Enum fields arrive as text and are parsed with `EnumCodes.FromCode`. Dates are ISO `yyyy-MM-dd`
(`DateOnly`). The whole detail is returned after writes, so the UI refreshes its version without
a second request.

**Scope guard**: the #4 guard test enumerates routes containing `/comparsas/{id}`. It is
generalised to a list of scoped route prefixes that adds `/arquebusiers/{id}`. A FiringChief with
no assignments must get `403` or `404` from every such route, never `2xx`. A second test covers a
FiringChief who is assigned elsewhere.

### D7. Audit entries

Recorded with `IAuditTrail.Record` in the same unit of work. `EntityType` is `Arquebusier` or
`OwnedWeapon`. `ComparsaId` is always the arquebusier's comparsa at the time of the change.

| Action | EntityId | Data |
|---|---|---|
| `ArquebusierRegistered` | arquebusier id | — (implementation note: no status or license flags, see below) |
| `ArquebusierUpdated` | arquebusier id | `{changedFields: ["nationalId", "phone", "license", …]}` |
| `ArquebusierTransferred` | arquebusier id | `{fromComparsaId, toComparsaId}` (ComparsaId = the source comparsa) |
| `ArquebusierDeleted` | arquebusier id | `{ownedWeaponCount}` |
| `OwnedWeaponAdded` | weapon id | `{arquebusierId, weaponModelId}` |
| `OwnedWeaponUpdated` | weapon id | `{arquebusierId, changedFields}` |
| `OwnedWeaponRemoved` | weapon id | `{arquebusierId, weaponModelId}` |

**Why only field names** (spec "Registry changes are audited"): the audit trail is append-only,
and erasure is the only exception (audit-privacy spec). If entries held DNIs, names or dates,
every deletion (BR-14) would leave personal data behind, and erasure would have to rewrite JSON
history. Field names still answer "who changed what and when" (SEC-05). The status, which says
whether the person fires, is not copied either: it counts as personal data once linked to an id.
The cost is that old values cannot be rebuilt from the audit trail. We accept that, because the
registry holds current data only (no license history, by design).

### D8. Frontend

Feature folder `src/features/arquebusier-registry/`:

| Route | Page | Access |
|---|---|---|
| `/arquebusiers` | `ArquebusiersPage` | signed in |
| `/arquebusiers/new` | `ArquebusierFormPage` | signed in |
| `/arquebusiers/:id` | `ArquebusierDetailPage` | signed in; 404 → `NotFoundPage` |
| `/arquebusiers/:id/weapons/new`, `/arquebusiers/:id/weapons/:weaponId` | `OwnedWeaponFormPage` | signed in |

- **`ArquebusiersPage`**: `PageHeader` with "Register arquebusier", a search `TextInput`
  (`type="search"`, inside `FormField`), and `FilterSelect`s for the comparsa (hidden when the
  user has one comparsa) and the status. The `DataTable` columns are: last name and first name
  (a link), nationalId, federationId, comparsa, a `StatusBadge` for `arquebusier`, and a
  `StatusBadge` for `license` or the text "No license". The search filters the fetched rows in a
  `useMemo`, comparing `normalize('NFD')` without diacritics and in lower case, against the name,
  nationalId and federationId. The search term is never put in the URL or sent to the server,
  because it may be a DNI. `EmptyState` covers a FiringChief without assignments and a scope with
  no arquebusiers yet.
- **Form** (`ArquebusierFields`, shared by create and edit), with `FormSection`s:
  - *Personal data*: comparsa (create only: a `SelectInput` of the active comparsas in scope,
    pre-selected when there is one), federationId, nationalId, first and last name, birth date,
    gender, email, phone and status;
  - *License*: type (none / AE / A-PROF), a "pending" `CheckboxField`, issued on and expires on.
    Expires-on is pre-filled when the issue date or type changes, until the user edits it;
  - *Training course*: completed on (empty = not done).

  The Zod schema mirrors the server rules, and the nationalId uses the D4 function. Server
  `errors{field}` are mapped onto the fields, and `code`s to `registry:errors.<code>`. A
  `409 modified` shows an `AlertBanner` and refetches the detail.
- **Detail page**: the form above (edit mode) and the *Owned weapons* section: a `DataTable` with
  `paginated={false}` showing the model label, translated kind/side/handedness/size, the weapon
  number, the guide number and edit and remove actions, with removal through `ConfirmDialog`.
  - The *Transfer* section is for Admins only: a `SelectInput` of the other active comparsas, then
    a `ConfirmDialog` that names both comparsas.
  - The *Delete* action uses a `ConfirmDialog` that names the arquebusier and says the deletion
    cannot be undone. Its description also reminds the user that an arquebusier who only stops
    firing is set to Reserve, and that registering them again later starts from empty data
    (`registry:delete.reserveHint`). After a deletion the page returns to the list with a notice.
  - An inactive comparsa shows an informational `AlertBanner`.
- **Owned weapon form**: a `SelectInput` of active weapon models, labelled by their Federation
  label, plus the current inactive model when editing a weapon that has one. Then weaponNumber and
  ownershipGuideNumber.
- **`DateInput`** is a new composite in `components/app/`: a native `<input type="date">` over the
  `ui` input, for `FormField`, with a story, a test and an axe check. The native picker is
  accessible and mobile-friendly (NFR-01). Read-only dates are formatted with `Intl.DateTimeFormat`
  in the active language.
- **Queries**: orval-generated hooks. Mutations invalidate `arquebusiers` and
  `arquebusiers/{id}`. Comparsas and weapon models come from the existing catalog hooks.
- **Navigation**: `{ to: '/arquebusiers', labelKey: 'nav.arquebusiers', icon: IdCard }` for every
  role, after Home. The icon is checked against lucide at apply time.
- **Status**: the existing `arquebusier` and `license` mappings are reused, and `docs/design/status.md`
  is unchanged. `EXPIRING` stays unused until #7.

**Rules for the screens (from the group 6 reviews, implementation notes):**
- Dates: `DateInput` reports a partly typed date as `INCOMPLETE_DATE`, so schemas show
  `registry:validation.date` instead of "required"; schemas also check `isIsoDate` and the bounds,
  with `todayIso()` (Europe/Madrid) for "not in the future". Optional dates (course, license dates)
  are `clearable`. Form values map `null` to `''` when loading and `''` to `null` on submit; the
  native picker follows the browser locale, read-only dates the app language.
- The computed expiry date is announced in a polite live region (`form.expiresOnComputed`) and is
  only recomputed while the user has not edited it themselves.
- Detail and weapon form pages render their form under `key={id}` / `key={weaponId}` with
  `values` and `resetOptions: { keepDirtyValues: true }`, as `ComparsaDetailPage` does, so moving
  between records never keeps the previous one's state. `OwnedWeaponFormPage` derives create or
  edit from `weaponId`, loads the arquebusier (404, inactive-comparsa notice), and offers a
  "back to the arquebusier" link and a cancel button: the breadcrumbs only reach the list.
- Delete and remove confirmations put "cannot be undone" first and the Reserve suggestion second,
  both inside the dialog description. The transfer title names the target comparsa.
- Server field reasons map to `registry:validation.<reason>`, except `weaponModelId: notFound`,
  which maps to `validation.modelNotFound`.
- Playwright (group 8) checks the date fields in Chromium, Firefox and WebKit: typing per segment,
  an incomplete date and an empty required one. A manual screen-reader pass is a release step.

**i18n**: a new namespace `registry` in `es-ES`, `ca-ES-valencia` and `en`:
- `arquebusiers.*`: list, search, filters, columns, empty states, form sections and fields with
  hints (nationalId leading zeros), actions, notices;
- `license.*`, `training.*`;
- `ownedWeapons.*`: section, form, remove confirmation;
- `transfer.*`, `delete.*`;
- the enums `gender.MALE|FEMALE|UNSPECIFIED` and `licenseType.AE|A_PROF`, plus `noLicense`;
- `errors.<code>` and `errors.validation.<reason>` for every D5 code and reason.

Weapon enum labels are reused from `catalog:kind.*` and the related keys. `common` gets
`nav.arquebusiers`. The typed-keys declaration is extended. The translation completeness test
covers the new namespace.

### D9. Synthetic seed

`RegistrySeeder` (`IDataSeeder`, Order 30) inserts rows with fixed ids and skips the ones that
exist:
- About 12 arquebusiers spread over the seeded comparsas Norte, Sur and Este, plus one in the
  inactive Oeste.
- The names are "Arcabucero Sintético Uno", "Arcabucera Sintética Dos" and so on.
- The DNIs are built from implausibly low numbers (`00000001`…, letter computed). There are two
  NIEs (`X00000…`). The emails use `@polvorapp.example` and the phones use `+34 600 000 0NN`.
- The data covers `ACTIVE` and `RESERVE`, AE and A_PROF licenses (valid, expired, pending) and no
  license, course done and not done, and owned weapons (a trabuco, an arcabuz, a pistol) with
  guides `SINT-0001`….
- The license dates are computed relative to the seed date, so "valid" and "expired" stay true
  over time.

The same non-local guard as the other seeders applies: outside Development and Testing, it refuses
a database that holds non-synthetic arquebusiers. E2E tests rely on these rows.

### D10. Deletion and transfer

- **Deletion** runs in one transaction:
  1. Lock the row with `SELECT … FOR UPDATE` through the scope filter.
  2. Count the owned weapons for the audit entry.
  3. Remove the arquebusier (owned weapons cascade) and commit.

  The audit entry holds no personal data (D7), so nothing personal remains. #6 adds the photo
  deletion to this flow (objects are deleted after commit; an orphan sweep catches failures).
  #10 adds the anonymisation of past entries. Each extends `ArquebusierAdministration.DeleteAsync`
  in its own change.
- **Transfer** runs in one transaction:
  1. Lock the arquebusier `FOR UPDATE`.
  2. Validate the target through `ICatalogDirectory`: it exists and is active, and differs from
     the current comparsa.
  3. Update `comparsa_id` and audit.

  The foreign key guarantees that the target still exists at commit. Owned weapons follow
  implicitly, because they hang off the arquebusier. A transfer racing with a *deactivation* of
  the target may still land there. This is accepted as in D5: the registry cannot lock the catalog
  row without reading another module's table, and the catalog's deactivation lock
  (`FOR NO KEY UPDATE`) does not conflict with the foreign key's `FOR KEY SHARE`.

**Locking and race rules (from the group 2 database review, implementation notes):**
- **Row locks never use `SELECT * … FOR UPDATE` through EF**: `SELECT *` omits the `xmin` system
  column, so the `Version` property cannot be materialised. Lock with
  `SqlQuery<int>("SELECT 1 AS \"Value\" FROM registry.arquebusiers WHERE id = {id} AND ({all} OR
  comparsa_id = ANY({ids})) FOR …")`, which reports whether the row exists in the caller's scope,
  then load it with a normal LINQ query. A row lock does not change `xmin`, so the loaded version
  stays valid. `RegistryHardeningTests` checks which lock modes conflict.
- **Owned-weapon writes lock their parent** with `SELECT 1 … FOR KEY SHARE` on the arquebusier, then
  re-check the scope. That conflicts only with the `FOR UPDATE` of a transfer or deletion, so a
  weapon change can never land on an arquebusier that just left the user's scope, and it does
  not block an edit of the arquebusier (`FOR NO KEY UPDATE`) or other weapon writes.
- **Foreign-key violations (23503) are mapped by constraint name** in every registry write, because
  the catalog check runs on another connection:
  - `fk_arquebusiers_catalog_comparsas` → `404 arquebusiers.comparsaNotFound` (registration and
    transfer racing with a comparsa deletion);
  - `fk_owned_weapons_catalog_weapon_models` → `400 validation weaponModelId: notFound`;
  - `fk_owned_weapons_arquebusiers_arquebusier_id` → `404 arquebusiers.notFound` (weapon added
    while the arquebusier is deleted).
- **Catalog lookups before the transaction** (group 4 reviews): the target comparsa of a transfer
  and the model of a weapon write are read before `BEGIN`, so no row lock is held while another
  connection queries the catalog and a request never needs two pooled connections at once. The
  foreign keys still decide a race with a catalog deletion.
- **Owned-weapon rows are not locked**: two removals or a removal and an edit of the same weapon
  race on its `xmin`; the loser answers `404 ownedWeapons.notFound` (removal) or
  `409 ownedWeapons.modified` (edit), never a 500.
- **Key updates**: an edit of `nationalId` or `federationId` changes a unique column, which
  PostgreSQL treats as a key update; that `UPDATE` waits for in-flight weapon writes (`FOR KEY
  SHARE`) on the same arquebusier, at worst until the lock timeout. There is no lock cycle. A
  deadlock between two edits swapping unique values is answered `503 registry.busy`, like a
  lock timeout.
- **Lock timeout**: registry transactions that lock rows or touch the cross-schema foreign keys
  run `SET LOCAL lock_timeout = '5s'`, as the catalog does, so a stuck catalog deletion cannot
  hold a registry request forever.
- **Why the usage check and the foreign key are race-free together**: an in-flight registry insert
  or update holds `FOR KEY SHARE` on the referenced catalog row, so the catalog's `FOR UPDATE`
  delete waits for it and then sees the row in its usage check. In the other order, the registry
  write waits and then fails with 23503 (mapped above). This holds only while the catalog's delete
  keeps taking `FOR UPDATE` first.

### D11. Security and GDPR

- **BR-12 / SEC-03**: the scope is enforced on the server for every read and write (D5). Out-of-scope
  requests get `404`, and the guard test covers every `/arquebusiers/{id}` route (D6). Transfer is
  Admin-only (`AuthorizationPolicies.Admin`).
- **SEC-05**: every write is audited in the same transaction, and no-ops are not (D7).
- **Data minimisation, BR-14, SEC-08, SEC-09**:
  - audit data holds no personal values;
  - deletion is a hard delete with cascade;
  - problem responses never echo submitted values or data of another arquebusier;
  - logs carry ids only, never nationalId, names or contact data (NFR-12; a test asserts that a
    409 duplicate does not log the value);
  - responses are `no-store`;
  - DNIs never appear in URLs or query strings.
- **Duplicate probing**: a FiringChief can learn that a DNI or guide number already exists
  somewhere in the Federation, because BR-02 requires it. They never learn where or whose. This is
  accepted: FiringChiefs are invited, 2FA-authenticated officers. To keep it slow and visible
  (group 3 security review), every registry write (registration, edit, transfer, deletion and the three owned-weapon writes) is limited to 60 per minute per user
  (`RateLimitPolicies.PersonalDataWrites`, `429`), and every rejected write is logged with the
  user id and outcome, never the values.
- **Request size**: registry routes accept at most 64 KB bodies (`RequestSizeLimit` metadata,
  enforced by Kestrel), and text fields far over their limit are refused before they are normalised.
- **SEC-11**: the seed is fully synthetic (D9). Test data uses the D4 vectors file and synthetic
  builders.
- **Input**: enums are whitelisted codes, lengths are bounded, dates are typed, and EF
  parameterises every query. Raw SQL appears only in the migration (FKs, checks) and in the row
  locks.

### Follow-ups from the group 3 reviews (not blocking)

- Fail startup outside local environments when the connection string sets
  `Include Error Detail=true`: PostgreSQL error details would then carry personal values into logs
  (NFR-12). Today it is off by default. This is a platform change.
- Resolve the Europe/Madrid time zone at startup (health check), so a host without tzdata fails
  fast instead of on the first registry request.
- Exports (#12) must neutralise spreadsheet formulas in free-text fields (names starting with
  `=`, `+`, `-` or `@`).
- After a deletion, audit entries and logs still carry the arquebusier and weapon UUIDs
  (pseudonymous, no personal values). Photos (#6), edition entries (#10), exports (#12), backups
  and log retention must not let a UUID be mapped back to the person (group 4 security review).
- The comparsa scope is read once per request, before the transaction: a FiringChief whose
  assignment is removed in the same milliseconds can land one last write. Accepted.
- A tighter per-user limit on duplicate rejections (guide, DNI, federation id) would make probing
  slower than the general 60 per minute; revisit if the Warning logs show probing.
- `xmin` can jump after `VACUUM FREEZE`; a client then gets one spurious `409 modified` and
  reloads. Accepted.

## Risks / Trade-offs

- [Cross-schema foreign keys couple the `registry` and `catalog` schemas at the database level] →
  They point one way (registry → catalog), follow migration order, and are documented in the
  modules README. No code reads another module's table. Dropping the catalog schema now requires
  dropping the registry schema first, which matches the dependency.
- [Audit entries cannot show old values] → Accepted for GDPR completeness (D7). If the Federation
  later needs value history for disputes, it needs an ADR on encrypted or erasable audit payloads.
- [Client-side search loads the whole scoped list] → It is at most about 800 small rows for an
  Admin (well under NFR-05). We revisit if the registry grows by an order of magnitude.
- [Optimistic concurrency forces a reload on conflicting edits] → This is rare, with a few
  FiringChiefs per comparsa. The UI explains it and keeps the user on the page with fresh data.
- [Strict 8-digit DNI rejects values typed without leading zeros] → The form hint and error say to
  add leading zeros. #8 import may pad on import.
- [The ID photo is mandatory in the data model but absent until #6] → #6 decides how existing
  records without a photo are handled (data-model note added in this change). Badges (#16) come
  after #6.
- [Editing a weapon model's kind or attributes changes how existing owned weapons read] → This is
  intended: catalogue corrections should propagate. There are no edition data yet. #10 snapshots
  what it needs for orders.
- [Duplicate probing of DNIs by a FiringChief] → Accepted (D11). Every registration attempt that
  succeeds is audited, and FiringChiefs are trusted, identified users.

## Migration Plan

1. Deploy: `migrate` applies `registry` after `catalog`. It only adds a schema, tables and
   foreign keys into existing catalog tables. Nothing existing changes.
2. After deploy, FiringChiefs register their arquebusiers (or the Admin imports them with #8).
   Before that, the registry is empty and the catalog behaves as today.
3. Rollback: redeploy the previous image. The `registry` schema is ignored by it, but its foreign
   keys still block deleting referenced comparsas or models (the catalog already maps that to
   `409 inUse`). Drop the `registry` schema manually if a full rollback is needed.
