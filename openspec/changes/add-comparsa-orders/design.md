# Design

## Context

See `proposal.md` for the motivation, and the delta specs for the behaviour
(`specs/comparsa-orders`, `specs/compliance-insights`, `specs/arquebusier-registry`,
`specs/festival-editions`). The facts below come from the current code and shape the approach.

- **Module anatomy** (`backend/src/Modules/README.md`, ADR-0001):
  - each module has an implementation project and a `.Contracts` project;
  - migration orders are audit 0, identity 10, catalog 20, registry 30 and editions 40;
  - cross-schema foreign keys only point from a later module to an earlier one, and use
    `ON DELETE NO ACTION`, added with `migrationBuilder.Sql`;
  - each `DbContext` gets its own connection from the shared `NpgsqlDataSource`, so two modules
    never share a transaction today.
- **Editions contract.** `IEditionDirectory` (`GetCurrentAsync`, `FindAsync`) returns
  `EditionSnapshot(Id, Year, Status, OrdersOpen, FestivalStartsOn, FestivalEndsOn,
OfferedWeaponModelIds)`:
  - `OrdersOpen` is true only for the edition in progress with open orders. It is the single
    definition of BR-10's window rule;
  - its doc comment says "#10 rereads inside its own write transaction";
  - `EditionLifecycle.SetOrdersAsync` takes the edition row `FOR UPDATE`;
  - `EditionAdministration.DeleteAsync` only checks `status = DRAFT`, and there is no editions veto
    contract.
- **Registry contracts.** `IArquebusierFacts` returns aggregate facts without ids or names, by
  design. No contract returns a per-arquebusier roster, owned weapons or a lookup by `nationalId`.
  The deletion (`ArquebusierAdministration.DeleteAsync`) cascades owned weapons and photos inside
  the registry. Its comment says that #10 adds the anonymisation of entries. That comment changes:
  entries are now kept as history (BR-14 amended).
- **Compliance contract.** `IComplianceRules.Evaluate(ComplianceFacts, DateOnly referenceDate)`
  evaluates every warning on one date.
- **Catalogue veto.** `ICatalogUsage` (`IsComparsaInUseAsync`, `IsWeaponModelInUseAsync`) is
  registered additively. Its doc already names "orders".
- **Conventions in use:**
  - `xmin` versions with `<entity>.modified`;
  - write guards that turn lock timeouts into a retryable 503;
  - PascalCase audit actions written with `IAuditTrail.Record` before `SaveChangesAsync`;
  - `InputFields` and `EnumCodes` for request parsing;
  - `RateLimitPolicies.PersonalDataWrites` (60 per minute per user) for requests that reveal
    whether a personal identifier exists.
- **Frontend:**
  - the `order` status mapping (`DRAFT`, `SUBMITTED`, `RETURNED`, `VALIDATED`) already exists in
    `components/app/status.ts` and `docs/design/status.md`;
  - `ConfirmDialog` accepts `children` and an initial focus target;
  - there is no multi-line text composite; `components/ui/textarea.tsx` exists as a primitive;
  - the seed has three active comparsas: Norte, Sur and Este.

## Goals / Non-Goals

**Goals:**

- One new module that owns orders, entries and loans, and reads everything else through
  contracts.
- Make BR-10's window atomic across two modules: no FiringChief order write commits after the
  orders close.
- Keep entries correct when the registry changes, without the registry depending on the orders'
  tables, and without registry writes failing because of orders.
- Expose the cross-comparsa data that loans need, and only that.

**Non-Goals:**

- No stored totals, warnings or first-year flags: everything is derived on read. At ~800 entries
  per edition, reading them all is cheap (NFR-05).
- No event bus or background sync between the registry and the orders.

## Decisions

### D1. A `ComparsaOrders` module with an `orders` schema

`PolvorApp.ComparsaOrders` and `.Contracts`, `MigrationOrder = 50`, schema `orders`, registered
like the editions module: `Program.cs`, `PolvorApp.slnx`, `Dockerfile` and `ModelDriftTests`.
Folders by feature: `Orders/`, `Entries/`, `Loans/`, `Totals/`, `History/`, `Seeding/`,
`Endpoints/` and `Persistence/`. Writes go through `OrderWriteGuard`, as in the other modules.

### D2. Data model

```
orders.comparsa_orders
  id uuid pk, edition_id uuid not null, edition_year int not null,
  comparsa_id uuid not null, status text not null (check DRAFT|SUBMITTED|RETURNED|VALIDATED),
  prepared_at timestamptz, prepared_by_user_id uuid,
  submitted_at timestamptz null, submitted_by_user_id uuid null, attested boolean not null default false,
  submitted_by_admin boolean not null default false,
  reviewed_at timestamptz null, reviewed_by_user_id uuid null, return_reason text null (<= 500),
  updated_at timestamptz not null, xmin
  unique ux_comparsa_orders_edition_comparsa (edition_id, comparsa_id)

orders.edition_entries
  id uuid pk, order_id uuid not null -> comparsa_orders on delete cascade, edition_id uuid not null,
  arquebusier_id uuid null, status text, powder_kg smallint (0..2), caps_boxes smallint (0..99),
  caps_type text null, weapon_source text, owned_weapon_id uuid null,
  rental_weapon_model_id uuid null, flask text, created_at timestamptz, xmin,
  -- history copy (spec "Entry history"), nullable so a GDPR erasure (#15) can blank it
  first_name text null, last_name text null, national_id text null, federation_id int null,
  owned_weapon_model_id uuid null, owned_weapon_number text null, owned_weapon_guide_number text null,
  copied_at timestamptz not null
  unique ux_edition_entries_edition_arquebusier (edition_id, arquebusier_id) where arquebusier_id is not null
  index (arquebusier_id), index (owned_weapon_id)

orders.weapon_loans
  id uuid pk, entry_id uuid not null unique -> edition_entries on delete cascade,
  lender_kind text (ARQUEBUSIER|EXTERNAL), lender_owned_weapon_id uuid null,
  -- the lender and the weapon, for both kinds: typed for EXTERNAL, copied for ARQUEBUSIER
  lender_first_name, lender_last_name, lender_national_id, lender_comparsa_id uuid null,
  weapon_model_id uuid, weapon_number, ownership_guide_number
  index (lender_owned_weapon_id)
```

**Check constraints** carry the BR-05 and shape rules:

- a `RESERVE` entry has 0 kg, 0 boxes, `NONE` and `NONE`;
- `caps_type` is set exactly when `caps_boxes > 0`;
- `rental_weapon_model_id` is set exactly for `RENTAL`;
- `owned_weapon_id` and the owned weapon copy are null unless `OWNED`;
- `lender_owned_weapon_id` and `lender_comparsa_id` are null for `EXTERNAL`.

The copy columns are nullable only so that a GDPR erasure (#15) can blank them. The service always
fills them; a test asserts it.

"Owned weapon removed" is the state `OWNED` (or an `ARQUEBUSIER` loan) with a null weapon id and
its copy still set, so the checks allow it. "No longer in the registry" is a null `arquebusier_id`
with the identity copy set.

**`edition_year`** is copied from the edition when the order is prepared. An edition's year never
changes (festival-editions spec), so the copy cannot go stale. It lets the pre-fill and the
first-year rule order editions without reading the editions schema. **`edition_id`** is
denormalised on entries for the "one entry per arquebusier per edition" index.

**Cross-schema foreign keys** (raw SQL, literal names, each asserted by a database test):

| Column                                                                                                            | Target                       | On delete    |
| ----------------------------------------------------------------------------------------------------------------- | ---------------------------- | ------------ |
| `comparsa_orders.edition_id`                                                                                      | `editions.festival_editions` | NO ACTION    |
| `comparsa_orders.comparsa_id`                                                                                     | `catalog.comparsas`          | NO ACTION    |
| `edition_entries.rental_weapon_model_id`, `edition_entries.owned_weapon_model_id`, `weapon_loans.weapon_model_id` | `catalog.weapon_models`      | NO ACTION    |
| `weapon_loans.lender_comparsa_id`                                                                                 | `catalog.comparsas`          | NO ACTION    |
| `edition_entries.arquebusier_id`                                                                                  | `registry.arquebusiers`      | **SET NULL** |
| `edition_entries.owned_weapon_id`, `weapon_loans.lender_owned_weapon_id`                                          | `registry.owned_weapons`     | **SET NULL** |

User ids (`prepared_by`, `submitted_by`, `reviewed_by`) carry no foreign key, as in the audit
trail. Users are deactivated, never deleted, and names are read through `IUserDirectory`.

### D3. History: a copy on the entry, and `ON DELETE SET NULL` for the live link

Entries are the edition's history (maintainer decision): never removed, and anonymised only on a
GDPR request. They must therefore read correctly after the arquebusier or the weapon leaves the
registry. Each entry and loan has two parts:

- **A live link** (`arquebusier_id`, `owned_weapon_id`, `lender_owned_weapon_id`). It is used
  while the record exists, for current names, the compliance facts, the ownership checks and the
  first-year rule. It is a foreign key with `ON DELETE SET NULL`. Deleting an arquebusier cascades
  to their owned weapons inside the registry, and PostgreSQL then nulls the links in the same
  transaction. For past editions, the registry needs no code. The deletion never fails on a
  constraint because of orders, although lock waits can make it a retryable 503 (see below).
- **A copy** of the identity (`first_name`, `last_name`, `national_id`, `federation_id`) and of the
  weapon (model, number, guide). The copy is taken from the roster (D5) when the entry is created,
  on every entry save, and for every entry of the order on submission and validation, while the
  link exists. Reads prefer the live data and fall back to the copy. Totals and #12's exports use
  the copy for records no longer in the registry.

The copy is refreshed only by order writes. If an arquebusier's name is corrected after the last
order write and they are then deleted, the copy keeps the earlier spelling. This is accepted: the
copy is what the order last stated. A refresh on read would need a write inside a `GET`.

The README gains the rule: **"a reference that must outlive its target uses `ON DELETE SET NULL`,
and the referencing module keeps a copy of what it needs and treats null as 'no longer there'"**.

**The edition in progress is different while its orders are open** (maintainer decision):
deleting an arquebusier removes their entry there. Once the orders are closed, the entry is kept
like any other history, and only an Admin can still change it (BR-10). `ArquebusierRegistry.Contracts` gains a participant contract:

```csharp
Task<ArquebusierDeletionEffect> OnDeletingAsync(Guid arquebusierId, IReadOnlyCollection<Guid> ownedWeaponIds, DbTransaction transaction, CancellationToken ct);
```

`ArquebusierAdministration.DeleteAsync` calls every registered implementation after locking the
arquebusier `FOR UPDATE` and before removing the row, on its own transaction (the D4 technique). It
records the returned effect, entry and order ids only, in its `ArquebusierDeleted` audit data.

The orders implementation keeps the module's lock order: edition, then orders by id, then entries
and loans. It runs these steps:

1. It reads the current edition with `ReadForOrderWriteAsync` on that transaction (`FOR SHARE`),
   so the closing of the orders or a status move serialises with it.
2. It locks `FOR UPDATE`, ordered by id, every order that holds an entry or loan linked to the
   arquebusier or to their owned weapons, in any edition. These are the rows that the deletion
   removes or that `SET NULL` updates. Order writers take the order lock first too, so the two
   cannot deadlock on entry rows (database review, group 2).
3. When `OrdersOpen` is false, it returns no effect, and `SET NULL` keeps the entry as history.
   Otherwise, it runs one `DELETE … RETURNING` on `orders.edition_entries` of that edition, which
   cascades its loan.
4. It bumps `updated_at` on the affected order, so a stale submit fails with `orders.modified`.

**Remaining cycle.** An order writer that holds an order lock and then inserts or updates a key to
the arquebusier needs `FOR KEY SHARE` on the arquebusier row for the foreign key check. A registry
deletion that holds that row `FOR UPDATE` and then waits for the order is the other side of the
cycle. The registry must lock the arquebusier first, so this cannot be reordered. PostgreSQL
detects the deadlock (`40P01`) and both guards turn it into a retryable 503
(`RegistryLocks.IsRetryable`, `OrderProblems.IsRetryable`). A concurrency test covers it (task 6.4).

**Owned weapon removed on its own.** The registry's owned-weapon delete has no participant. Its
`SET NULL` waits for row locks held by order writers, at most until the 5 s lock timeout. It can
also meet the same cycle as above, a writer holding the entry and waiting for `KEY SHARE` on the
weapon, which ends as a detected deadlock. Either way, it ends as a retryable 503 and never fails on
a constraint.

**Side effects on writers:**

- The `SET NULL` update bumps the entry's `xmin`, so an edit that is in flight on that entry gets
  `entries.modified` and the UI reloads it.
- An order write whose foreign key to `registry.arquebusiers` or `registry.owned_weapons` fails
  with `23503` lost a race with a registry deletion. It answers `409 orders.modified`, so the UI
  reloads the order.

The SQL lives in the orders module, which owns those tables. Then the registry deletes the row,
and `SET NULL` handles every other edition. The comment of `DeleteAsync` changes accordingly.

_Alternatives considered for the current entry_:

- Leaving it unlinked, as in the history, while the orders are open. The maintainer wants it
  gone, with a warning first, while the order can still change.
- Refusing the deletion while the arquebusier has a current entry. BR-14 requires that the
  person can always leave.

_Alternatives considered_:

- A copy only, without the live link. Names and warnings would be stale while the arquebusier
  exists, and ownership checks would need another key.
- A hook that the registry calls inside its delete, to copy the data at that moment. Each module
  has its own connection, so it could not write in the registry's transaction. It would also put
  orders code on the registry's core path.
- `NO ACTION` plus a veto. It would block deletions, which BR-14 forbids.
- Anonymising on deletion (the first draft of this change). The maintainer rejected it: the
  history of past editions must keep who received what.

### D4. BR-10 atomicity: an edition read on the caller's transaction

`IEditionDirectory` gains:

```csharp
Task<EditionSnapshot?> ReadForOrderWriteAsync(Guid editionId, DbTransaction transaction, CancellationToken ct);
```

The editions module runs `SELECT … FROM editions.festival_editions WHERE id = @id FOR SHARE` on
`transaction.Connection`, enlisted in `transaction`. The SQL stays inside the module that owns the
table, but the lock belongs to the caller's transaction. Every order write:

1. begins its transaction (`OrderWriteGuard`, `lock_timeout` 5 s);
2. reads the edition with `ReadForOrderWriteAsync`;
3. checks BR-10 on that snapshot.

`SetOrdersAsync` takes `FOR UPDATE` on the same row, so closing the orders waits for in-flight
order writes, and no FiringChief write commits after the closing. This is the same pattern as the
registry lock (`registry_settings` `FOR SHARE` / `FOR UPDATE`). A status move and a deletion also
take `FOR UPDATE`, so they serialise with preparations.

The offered models in the snapshot are read through the catalogue on its own connection. A model
deactivated concurrently is caught at submission, where the entry shows `rentalModelNotOffered`.

`FestivalStartsOn`'s doc comment is corrected to the D7 reference dates.

_Alternatives considered_:

- Reading the edition outside the transaction. It leaves a window in which an edit commits after
  the close.
- Raw SQL on `editions.*` from the orders module. It breaks "no code queries the other schema".
- A shared `TransactionScope`. It needs distributed transactions across two connections, which
  Npgsql does not do safely.

### D5. Contracts

**`ArquebusierRegistry.Contracts.IArquebusierRoster`** (new), server-side only:

- `ListByComparsaAsync(comparsaId)` returns `RosterArquebusier(Id, ComparsaId, FirstName,
LastName, NationalId, FederationId, Status, BirthDate, License, TrainingCompletedOn, HasIdPhoto,
IReadOnlyList<RosterWeapon> Weapons)`, sorted like the registry list (Spanish collation). The
  compliance facts are flattened, because a Contracts project references only SharedKernel; orders
  build `ComplianceFacts` from them;
- `FindManyAsync(ids)` reads the same data for loans and for the entries of transferred
  arquebusiers;
- `FindLenderAsync(normalisedNationalId)` returns `LenderSummary(ArquebusierId, FirstName,
LastName, NationalId, ComparsaId, Weapons)` or null. Its weapons are `LenderWeapon(Id,
WeaponModelId, WeaponNumber)`, with no ownership guide, so the guide cannot leak through a careless
  mapping. It is the only lookup by `nationalId`;
- `FindOwnedWeaponsAsync(ids)` returns `RosterWeapon(Id, OwnerId, WeaponModelId, WeaponNumber,
OwnershipGuideNumber)`;
- `IsNationalIdRegisteredAsync(normalisedNationalId)`.

The national ids and ownership guides are returned only so that the orders module can keep its
history copy (D3), and for #12's exports. The order responses never show the guide of a registered
owner, and the lookup response never shows the guide (D9). No method returns birth dates or
contact data, except the compliance facts that the warnings need.

**`ArquebusierRegistry.Contracts.IArquebusierDeletionParticipant`** (new; D3). Orders implement
it to remove the current-edition entry inside the registry's delete transaction.

**`ArquebusierRegistry.Contracts.ArquebusierFacts`** gains `Guid ArquebusierId`, so statistics can
join the first-year set. The contract stays server-side; no response exposes it.

**`ComplianceInsights.Contracts.IComplianceRules`** gains `EvaluateForFestival(ComplianceFacts
facts, DateOnly startsOn, DateOnly endsOn)`. It applies the D7 dates and drops
`LICENSE_EXPIRING`, reusing the existing rule functions so both paths share one implementation.

**`ComparsaOrders.Contracts.IParticipationHistory`** (new):

- `FirstYearAsync(int editionYear, IReadOnlyCollection<Guid> arquebusierIds)` returns
  `FirstYearResult(bool Known, IReadOnlySet<Guid> FirstYearIds)`.
  `Known` = any order with `edition_year < editionYear` exists. The first-year ids are those
  without an `ACTIVE` entry in such orders;
- `GetDeletionImpactAsync(Guid arquebusierId, IReadOnlyCollection<Guid> ownedWeaponIds)` returns
  `DeletionImpact(CurrentEntry? { EditionYear, ComparsaId, OrderStatus, WillBeRemoved }, int
LentWeaponsInCurrentEdition, bool HasPastEntries)`, for the delete confirmation (registry spec).
  The registry passes the owned weapons it already read, so the orders module needs no registry
  read for it. `WillBeRemoved` is the
  edition's `OrdersOpen` at read time. The participant rereads it under `FOR SHARE`, so a closing
  in between makes it keep the entry, which is the safe side.

The registry detail and the compliance statistics call it with the current edition's year (from
`IEditionDirectory`). Both are contract references, which the dependency rules allow in either
direction (README, "Contracts in both directions").

**`FestivalEditions.Contracts.IEditionUsage`** (new veto, like `ICatalogUsage`):
`IsEditionInUseAsync(editionId)`. `EditionAdministration.DeleteAsync` asks every implementation
after taking `FOR UPDATE` and answers `409 editions.inUse`. A `23503` on
`fk_comparsa_orders_edition` maps to the same code. `OrdersCatalogUsage : ICatalogUsage` reports
comparsas with orders or loan copies, and weapon models used by rentals, owned weapon copies or
loans.

### D6. API

All routes sit under `/api/comparsa-orders`, outside `/api/editions/{id}/orders`, which the
editions module uses to open and close the orders.

| Method and route                                               | Who   | Purpose                                                                                                                                      |
| -------------------------------------------------------------- | ----- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| `GET /comparsa-orders/overview?editionId=`                     | both  | Overview; the current edition when omitted, `{ edition: null }` when there is none                                                           |
| `POST /comparsa-orders` `{ editionId, comparsaId }`            | both  | Prepare (201)                                                                                                                                |
| `GET /comparsa-orders/{id}`                                    | both  | Order with entries, not-in-order list, lent-out list, totals, `canEdit` and the reason when not                                              |
| `POST /comparsa-orders/{id}/entries` `{ arquebusierId }`       | both  | Add an arquebusier; returns the order                                                                                                        |
| `PUT /comparsa-orders/{id}/entries/{entryId}`                  | both  | Edit an entry with its `version`; returns the order, so the client sees its new status, version and totals at once                           |
| `POST /comparsa-orders/lender-lookup` `{ nationalId }`         | both  | Lender lookup (D9)                                                                                                                           |
| `POST /comparsa-orders/{id}/submit` `{ version, attestation }` | both  | Submit; a FiringChief attests while the orders are open, an Admin submits on the comparsa's behalf at any time, and `attestation` is ignored |
| `POST /comparsa-orders/{id}/validate` `{ version }`            | Admin | Validate a `SUBMITTED`, `DRAFT` or `RETURNED` order                                                                                          |
| `POST /comparsa-orders/{id}/return` `{ version, reason }`      | Admin | Return                                                                                                                                       |

The entry body is `{ version, status, powderKg, capsBoxes, capsType, weaponSource, ownedWeaponId,
rentalWeaponModelId, loan, flask }`. `loan` is `{ ownedWeaponId }` or `{ external: { firstName,
lastName, nationalId, weaponModelId, weaponNumber, ownershipGuideNumber } }`.

There is no route to delete an order or an entry: both are history (spec).

Each entry response carries:

- `arquebusier`: the live data, or the copy when `inRegistry` is false;
- `warnings`;
- `firstYear` (null when unknown);
- `issues`, which block (`ownedWeaponMissing`, `loanWeaponMissing`, `rentalModelNotOffered`).

An `ACTIVE` entry without powder or without a weapon has no issue and no flag. The order response
carries `submittedByAdmin`.

The entry panel's choices come with the order (task 9.2): the order carries `offeredModels` (the
edition's rental models now, by label) and each entry `ownedWeapons` (the arquebusier's weapons now,
without ownership guides; empty once they left the registry). A FiringChief could not read a
transferred arquebusier or the edition's models otherwise.

Problem codes:

- `orders.notFound`, `orders.alreadyPrepared`, `orders.editionNotStarted`,
  `orders.comparsaInactive`, `orders.closed`, `orders.validated`, `orders.invalidTransition`,
  `orders.modified`;
- `orders.entriesInvalid`, with `entries: [{ entryId, reasons[] }]`;
- `orders.alreadyInEdition`;
- `entries.notFound`, `entries.modified`;
- `orders.busy` (503);
- `validation` with field keys such as `loan.nationalId`;
- `editions.inUse`, on the editions side.

Out of scope or a draft edition for a FiringChief is `404`. The review routes require `ADMIN`.
The submit route branches on the role. For a FiringChief, it needs open orders and
`attestation: true`, and records `attested = true`. For an Admin, it records
`submitted_by_admin = true` (a column on `comparsa_orders`) and `attested = false`.

### D7. Festival-date warnings

`EvaluateForFestival` applies these dates:

- the license status on `endsOn`: `LICENSE_EXPIRED` when `expiresOn < endsOn`;
- `UNDER_AGE` on `startsOn`;
- course and photos as they are now;
- no `LICENSE_EXPIRING`.

Orders call it only for `ACTIVE` entries of an `IN_PROGRESS` edition whose arquebusier is still in
the registry. Other
entries get an empty list (spec: warnings of past editions would reflect today's data).

### D8. Write pipeline and status rules

`OrderAdministration` and `EntryAdministration` follow one shape inside `OrderWriteGuard`:

1. Begin the transaction and read the edition (D4). For FiringChiefs, require
   `snapshot.OrdersOpen`, else `orders.closed`. For Admins, require a non-draft edition, else
   `orders.editionNotStarted`.
2. Lock the order row `FOR UPDATE`, with the comparsa scope in the `WHERE` clause (a `404` out of
   scope). A FiringChief editing a `VALIDATED` order gets `orders.validated`.
3. Apply the change, validate it with `EntryInput.Read` (pure, unit-tested) and the roster (owned
   weapon ownership, lender, registered national id).
4. Refresh the entry's history copy (and its loan's) from the roster while the links exist (D3).
5. When a FiringChief edits a `SUBMITTED` order, set it to `DRAFT`. Bump `updated_at` so the
   order's `xmin` changes, which makes a submit based on a stale view fail with
   `orders.modified`.
6. Record the audit entry, then `SaveChangesAsync`.

Status moves use a pure `OrderStatusMoves.Check(from, to, role)`:

- a FiringChief submits from `DRAFT` or `RETURNED`;
- an Admin submits from `DRAFT` or `RETURNED`;
- an Admin validates from `SUBMITTED`, `DRAFT` or `RETURNED`;
- an Admin returns from `SUBMITTED` or `VALIDATED`.

Submission and validation refresh the copies of every entry and loan of the order in the same
transaction.

**Entry issues** are computed by `EntryIssues.Of(entry, offeredModelIds)`. They are used by the
response, the submission and the validation, so the checks are the same everywhere. A missing
owned weapon is an issue only while the entry's arquebusier is in the registry.

**Preparation** reads the roster and the previous entries in one query: the entries of these
arquebusiers in orders with `edition_year < year`, picking the highest year per arquebusier. It
builds the entries with `Prefill.For(arquebusier, previous, offeredModelIds)` (pure). It then
inserts the order and all entries in one transaction. The unique index answers
`orders.alreadyPrepared` under races. Arquebusiers who already have an entry in the edition are
left out by the query, and the entry index rejects the rare race with another comparsa's
preparation, which the guard answers as `orders.busy` (retryable).

### D9. Loans and the lender lookup

- The lookup is a `POST` with the DNI in the body, so the identifier never appears in URLs,
  access logs or browser history.
- It normalises and validates with the registry's BR-01 validator (`SharedKernel`, or a contract
  if it lives in the registry: task 1.1 checks).
- It uses `RateLimitPolicies.PersonalDataWrites`. Its summary is widened to "requests that reveal
  whether a personal identifier exists".
- It records `LoanLenderLookedUp` with `{ found }` and no identifier, in its own small transaction.
  It is the only audited read, because it is the only one that reveals data outside the user's
  comparsa (BR-12, SEC-05).
- Every loan stores the lender and the weapon in the same columns. For an external owner, the
  values are typed, following the arquebusier name rules with the guide upper-cased. For a
  registered owner, they are copied from the roster and refreshed like the entry (D3). Replacing
  or leaving the loan while the order is editable deletes the row. That is an edit of the current
  order, not a loss of history.
- **Lent-out list.** It comes from the order's roster weapon ids, joined to `weapon_loans` of the
  edition. The borrower's name and comparsa are read with `FindManyAsync` and `ICatalogDirectory`.

### D10. Totals and the dashboard

`OrderTotals.Of(entries, entriesWithWarnings)` is pure and unit-tested; the order response carries
its totals. The overview (`OrderOverview`) loads the edition's orders and entries in one query each,
the registry facts of the linked `ACTIVE` entries with one `FindManyAsync` (only for the edition in
progress) and the catalogue labels, then computes per-order and edition totals in memory. Admin
rows cover the active comparsas plus those with an order. FiringChief rows cover their scope, with
no status counts or edition totals. Each row tells whether the caller may prepare the order now.
Status counts have a `notPrepared` field next to one per status.

### D11. Audit actions

| Action                                    | Entity          | Data                                                                                             |
| ----------------------------------------- | --------------- | ------------------------------------------------------------------------------------------------ |
| `ComparsaOrderPrepared`                   | `ComparsaOrder` | edition id, entry count                                                                          |
| `EditionEntryAdded`                       | `EditionEntry`  | order id, arquebusier id                                                                         |
| `EditionEntryUpdated`                     | `EditionEntry`  | order id, changed field names, `{ previous, current }` order status when it went back to `DRAFT` |
| `ComparsaOrderSubmitted`                  | `ComparsaOrder` | `{ previous, current }`, `attested` or `byAdmin`, `entriesWithWarnings`                          |
| `ComparsaOrderValidated`                  | `ComparsaOrder` | `{ previous, current }`, `entriesWithWarnings`                                                   |
| `ComparsaOrderReturned`                   | `ComparsaOrder` | `{ previous, current }` (no reason text)                                                         |
| `LoanLenderLookedUp`                      | `WeaponLoan`    | `{ found }`                                                                                      |
| `ArquebusierDeleted` (registry, extended) | `Arquebusier`   | the existing counts, plus the removed current entry id and its order id, when there was one      |

Every entry carries the order's comparsa id, so the #15 viewer can filter by comparsa. No entry
holds names, national ids, weapon numbers or guides. Entry values (kg, caps) are not recorded,
following the registry's "field names only" rule.

### D12. Frontend

`features/comparsa-orders/`:

- `pages/OrdersOverviewPage` serves `/orders` (current edition) and
  `/editions/:editionId/orders`. Admins get the dashboard (`StatCard`s per status, an edition
  totals `SectionCard` with a `Breakdown` per model, and a `DataTable` of comparsas). FiringChiefs
  get the list, and a FiringChief with one prepared order is redirected to it;
- `pages/OrderPage` serves `/orders/:orderId`: `RecordHeader` (comparsa logo, edition, `order`
  status badge, `orders` badge), `KeyFacts` totals, `AlertBanner`s (return reason, warnings,
  blocking issues, read-only reason, submitted by an Admin), an entries `DataTable` with
  `mobileRow` and a "No longer in the registry" mark (no remove action), and `SectionCard`s
  "Not in the order" and "Lent to others";
- `components/EntryEditSheet` (`EditSheet`, `RadioCards` for status and powder, `TextInput` for
  boxes, `SelectInput` for caps type, weapon and flask) and `components/LoanFields` (DNI
  `TextInput`, lookup button, weapon `RadioCards` or the external owner fields);
- `components/SubmitDialog`: a `ConfirmDialog` with the warnings list, plus a `CheckboxField` for
  FiringChiefs. Admins get the "on behalf of the comparsa" wording instead of the attestation;
- `components/ValidateDialog`, which says when the comparsa did not submit the order;
- `components/ReturnDialog`, a `ConfirmDialog` with the new `TextArea`;
- `entrySchema.ts` (Zod, mirroring `EntryInput`), `problems.ts`, `queries.ts`, `test-data.ts`.

Other changes:

- **New composite** `components/app/TextArea` over the `textarea` primitive, with a counter for
  `maxLength`, its story and an axe test (the catalogue test requires both). `docs/design` gains
  it, and an "Order page" pattern in `patterns.md`;
- **Navigation:** an `orders` item for both roles (icon `ClipboardList`) between Editions and
  Statistics. The route breadcrumbs are `nav.orders`;
- **Elsewhere:**
  - the edition detail links to its orders (non-draft);
  - the arquebusier detail shows a "First year" badge in its header when `firstYear` is true;
  - the delete confirmation reads `deletionImpact` from the arquebusier detail. It shows a
    warning `AlertBanner` that names the edition, the comparsa and the order status of the entry
    that will be deleted, or an info note that it stays as history when the orders are closed. It
    also lists the lent weapons that will show as removed, and says that past entries are kept;
  - the statistics page adds the first-year table, or the unknown message;
- **i18n:** a new `orders` namespace in three locales: `overview.*`, `order.*`, `entry.*`,
  `loan.*`, `submit.*`, `review.*`, `totals.*`, `issues.*`, `errors.*`. Also new
  keys `registry:detail.firstYear`, `registry:delete.currentEntry`, `registry:delete.lentWeapons`,
  `registry:delete.historyKept`,
  `insights:statistics.firstYear.*`, `editions:detail.orders`, `editions:errors.inUse` and
  `common:nav.orders`. Warning labels reuse the `insights` keys.

### D13. Seed and end-to-end isolation

`OrderSeeder : IDataSeeder` runs after the edition seeder. It creates:

- `VALIDATED` orders of the closed past edition for Norte and Sur, with fixed ids, leaving some
  arquebusiers without an `ACTIVE` entry so "first year" shows;
- for the current edition, Norte `SUBMITTED` and Sur `DRAFT`, with Este not prepared;
- a loan from an owned weapon of a Sur arquebusier to a Norte entry, and an external loan with a
  synthetic DNI that passes BR-01;
- in the past edition, an entry with a null `arquebusier_id` and a synthetic identity copy, as
  history of an arquebusier no longer in the registry;
- `ACTIVE` entries with 0 kg and an owned weapon, and with 2 kg and no weapon.

Every insert is skipped when its id exists.

E2E flows that change order state run in the `serial-state` project. They work from whatever
state they find:

1. the FiringChief prepares the order if needed, edits an entry and submits;
2. the Admin returns it;
3. the FiringChief fixes it and resubmits;
4. the Admin validates;
5. `afterEach` returns it, so the next run can edit it again.

The read-only checks (overview, past edition, first-year badge) run in the main projects against
the seeded orders, which nothing changes.

### Review notes (group 2)

- **Schema hardening.** The migration was regenerated before anything shipped. It adds:
  - a composite key from entries to `(order id, edition id)`, so the denormalised edition cannot
    drift;
  - a check for the return reason, which exists exactly while the order is `RETURNED`;
  - a check that a submission is attested or made by an Admin, never both;
  - a check that a linked entry holds its identity copy;
  - a check that a registered loan names the lender's comparsa;
  - partial indexes on every foreign key to the catalogue and the registry.

  `OrderLocks.LockOrderAsync` carries the comparsa scope, like the registry's locks.

- **Concurrency.** The participant's lock order, the remaining retryable cycle and the writer's
  `23503` handling are in D3. They are tested in task 6.4.

### Review notes (group 3)

- **Contracts.** The roster's unscoped methods (`FindManyAsync`, `FindOwnedWeaponsAsync`) take only
  ids read from stored records, never ids from a request. `FindLenderAsync` and
  `IsNationalIdRegisteredAsync` are cross-comparsa existence checks, reserved for the audited lookup
  and the loan write (D9). The remarks say so, and tasks 4.4 and 5.x test the out-of-scope ids.
- **Accepted.** `ReadForOrderWriteAsync` reads the offered models on the editions and catalogue
  contexts' own connections while the caller holds its locks. That is two extra pooled connections
  for a short read, bounded by the lock timeout. A lean variant for the deletion participant is not
  worth a second contract method at this scale.
- **Tests.**
  - The window tests poll `pg_stat_activity` for the lock wait instead of sleeping, and cover the
    converse (a write during a closing sees it closed) and the timeout.
  - The participant test checks from a second connection that the row is already locked.

### Review notes (group 5)

- **The `lenderRegistered` refusal reveals what the lookup reveals.** A loan write with an external
  owner whose DNI is registered answers it, so the refusal is recorded like a lookup,
  `LoanLenderLookedUp { found: true, via: "externalLoan" }`, without the identifier. It stays under
  the order-write rate limit. That is accepted: it tells only that the DNI is registered (no name,
  comparsa or weapons), and every probe is audited.
- **An unchanged external owner is kept without re-checking it.** An owner who joins the registry
  later does not make every later edit of that entry fail.
- **The lookup needs an assigned comparsa** for a FiringChief (404 otherwise), as well as open orders.
- **Accepted.**
  - The lookup shares the `PersonalDataWrites` budget (60 per minute) with the registry writes.
  - A registered loan accepts any owned-weapon id, because UUIDv7 ids cannot be guessed.
- **Retention.** The lender copies (`WeaponLoan.Lender*`, number and guide) are covered by SEC-08's
  history retention and by #15's erasure, like the entry copies.

### Review notes (group 4)

- **Loans landed with the entry write** (task 5.2, brought forward). A `LOAN` source without a
  loan row must never be saved. `LoanWriter` runs in the entry's transaction:
  - it deletes the loan when the source leaves `LOAN`;
  - it replaces a changed lender;
  - it keeps the current loan when the request sends no lender (`LoanInput.Keep`), so the other
    values of a `LOAN` entry can be edited without resending it;
  - it refuses an unknown weapon (`loan.ownedWeaponId: notFound`), a registered owner typed as
    external (`lenderRegistered`) and an inactive external model.
- **Removed weapons stay editable.** An `OWNED` entry whose weapon left the registry keeps `OWNED`,
  with its copy, when the request sends no weapon (`EntryContext.KeepsRemovedWeapon`). It still
  blocks a submission while its arquebusier is in the registry (task 6.1).
- **Races.** `OrderSaves.SaveAsync` maps every lost race the same way for every write:
  - a concurrency-token mismatch is `entries.modified`;
  - the unique indexes are `alreadyPrepared` or `alreadyInEdition`;
  - a `23503` on a registry or catalogue key is `orders.modified`.
- **Logging.** The scope checks run inside the write guard, so out-of-scope probes are logged
  (ids only).

### Review notes (group 6)

- **Moves answer `orders.modified` for every lost race.** A concurrency-token mismatch on a submit,
  validate or return is `orders.modified`, not `entries.modified` (`SaveAsync`'s `onConcurrency`).
  An owned weapon removed between the entries read and the registry read (weapon removals take no
  order lock) ends the move the same way, instead of a 500.
- **Copies move only when they change.** `CopiedAt` of an entry or a loan is set only when a copied
  value differs, so a move does not rewrite unchanged rows nor bump their versions under an open
  edit form.
- **Loans are refreshed in one batch** (`LoanWriter.RefreshManyAsync`): two registry reads per move,
  whatever the number of loans, while the order is locked.
- **Reads tolerate a concurrent deletion.** The order view and the overview take no lock, so an
  arquebusier deleted between their reads shows as no longer in the registry, as it will once the
  link is nulled. A move holds the order lock, which the deletion takes before unlinking, so a
  missing arquebusier there is a broken invariant and throws.
- **The overview throws for an order whose comparsa the catalogue does not return**, so the rows
  always add up to the edition totals.
- **Accepted, for the maintainer.**
  - An entry of an arquebusier who transferred out shows their current registry data and warnings
    to the old comparsa (BR-13 keeps the entry there). Showing the copy instead is a possible
    minimisation.
  - The order and overview reads are not rate-limited; only writes are.
  - The NFR-05 overview test measures wall-clock time with a 2 s budget for 800 entries; it runs
    well under it locally.
- **API.** A missing entry version is `400 version: required`. The order writes use a new per-user
  rate limit, `RateLimitPolicies.OrderWrites` (120 per minute, `RateLimits:OrderWrites:PermitLimit`).
- **Inconsistencies throw.** A non-null registry link without an arquebusier, and an `OWNED` weapon
  id that its arquebusier does not have, break design D3. They throw instead of being shown as "no
  longer in the registry".
- **Structure.** `EntryHistory` holds the queries a new entry needs. The pre-fill reads only the
  value columns of past entries, never their history copy.

### Research notes (task 1.1)

Versions: EF Core 10.0.12, Npgsql / Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, Zod 4.6.5,
React Hook Form 7.89.0 with `@hookform/resolvers` 5.9.1, Playwright 1.63.0.

- **Commands on another module's transaction (D3, D4).** EF Core documents sharing an external
  `DbTransaction`: create a command with `transaction.Connection.CreateCommand()` and set
  `command.Transaction`. Npgsql ignores `NpgsqlCommand.Transaction`, because PostgreSQL allows one
  transaction per connection and every command on it runs inside that transaction. So the command
  only needs the caller's connection. The implementations still set `Transaction`, for clarity
  and for the provider-agnostic contract. They use positional parameters (`$1`), never open,
  close or dispose the connection, and the caller obtains the transaction with
  `db.Database.CurrentTransaction!.GetDbTransaction()`.
- **Partial unique index.** `HasIndex(e => new { e.EditionId, e.ArquebusierId }).IsUnique()
.HasFilter("arquebusier_id IS NOT NULL")`, named so a `UniqueViolation` maps by constraint name,
  as `ux_festival_editions_in_progress` does.
- **Delete rules in tests.** `information_schema.referential_constraints.delete_rule` returns
  `SET NULL` or `NO ACTION` by `constraint_name`. The database test reads it for every
  cross-schema key.
- **Frontend forms.** The features use one flat `z.object` with `superRefine` for conditional
  rules (`arquebusierSchema`, `editionSchema`), not discriminated unions, because a flat shape is
  what React Hook Form binds. `entrySchema` follows that: the weapon source, the loan kind and
  `RESERVE` drive `superRefine` issues on the dependent paths. The panel resets the dependent
  fields with `setValue` when the status becomes `RESERVE`.
- **Text area.** `components/ui/textarea.tsx` (shadcn) is the primitive. The composite shows the
  counter as part of the description (`aria-describedby`). It announces it through a polite live
  region only near the limit, to avoid announcing every keystroke.
- **BR-01 validator.** `NationalId` is `internal` to the registry
  (`ArquebusierRegistry/NationalIds/NationalId.cs`). It is a pure rule shared with the frontend
  test vectors, so it moves unchanged to `SharedKernel.Validation.NationalId` (public). The
  registry and orders both use it. The frontend already has `features/arquebusier-registry/nationalId.ts`,
  which orders import. `SharedKernel` stays free of module references.

## Risks / Trade-offs

- **[`SET NULL` breaks the README's NO ACTION rule]** → It is documented as a second, named
  convention with its reason. A database test asserts each constraint's delete rule.
- **[Order history keeps personal data of people deleted from the registry]** (name, DNI/NIE,
  federationId, weapon numbers and guides; a change to SEC-08 and BR-14) → This is the maintainer's
  decision: the history must keep who received powder and weapons. Mitigations:
  - the copy is limited to what the order and the later exports need;
  - it is shown only within BR-12;
  - it lives in nullable columns that #15's erasure blanks on request (UC-26).

  `docs/compliance.md` records the retention. It flags the legal basis (records owed to the Arms
  Authority) and the privacy notice for the Federation (Q-50).

- **[Deleting an arquebusier changes a submitted or validated order of the edition in progress
  without the comparsa or the Admin re-reviewing it]** → This only happens while the orders are
  open, when the order can still change anyway. Once they close, the entry is kept. The
  confirmation names the order and its status, and the removal is in the audit trail with the
  order id.
- **[The deletion participant runs orders SQL inside the registry's transaction]** → It is one
  statement on the orders' own tables, bounded by the same 5 s lock timeout. A failure aborts the
  whole deletion, which the registry guard turns into a retryable `registry.busy`. Nothing is half
  deleted.
- **[The history copy can lag behind a registry correction]** → It is refreshed on every order
  write, submission and validation, so it matches what the order last stated (D3).
- **[The lender lookup enables probing of national ids across comparsas]** → It needs a valid
  check letter, is rate-limited per user, is audited, and returns no ownership guide, birth date,
  contact or license data. Only users who can edit an order may call it. The audit makes abuse
  visible to the Federation.
- **[External owners are personal data of non-members]** → Only what the Arms Authority will need
  is stored. It is kept with the order's history like the other copies, and replaced only while
  the order is editable. `docs/compliance.md` lists it (inventory, SEC-08 retention with the
  edition) and flags it for the privacy notice (Q-50) and the GDPR tooling (#15).
- **[`ReadForOrderWriteAsync` runs on a connection it does not own]** → It only reads one row and
  never commits or disposes the connection. An integration test proves that closing the orders
  waits for an in-flight order write, as `EditionRaceTests` does for the editions.
- **[The overview reads every entry of the edition]** → Under 1,000 rows. A test seeds 800 entries
  and asserts the response time stays well under NFR-05.
- **[The warnings use today's registry data on fixed festival dates]** → This is intended for the
  edition in progress. Past editions show no warnings (spec).
- **[The first-year flag depends on orders existing]** → A FiringChief who never prepares an order
  makes their arquebusiers look like first-timers the next year. The Admin dashboard shows
  unprepared comparsas, so the gap is visible.

## Migration Plan

1. Deploy: run `migrate`. The `orders` schema is new. The registry, editions and catalogue gain
   no columns, only incoming foreign keys and contract methods.
2. Rollback: revert the release and drop the `orders` schema. No other schema changes.
3. Production starts with no orders. The first-year flag stays unknown until the second edition
   used in PolvorApp, as the spec states.

## Open Questions

None. The BR-01 validator's location was settled in task 1.1 (research notes).

### Review notes (group 7)

- **Accepted, spec-mandated.** After a transfer (BR-13), the delete confirmation of the new
  comparsa's FiringChief names the previous comparsa and its order status, because the entry sits in
  that order. It concerns one person already in their scope, and the comparsa name is public.
- **Accepted.** The arquebusier detail reads the first year and the deletion impact on every
  response, the write responses included: a few indexed queries on small tables. The statistics'
  first-year cells, like the other gender cells, are not suppressed in a very small scope (SEC-05).
- The first-year sets are frozen, so the shared "unknown" value cannot be changed by a caller.

### Review notes (group 9)

- **The order cache holds orval's response.** `orderChanged` writes `{ data, status, headers }`, as
  `GET /comparsa-orders/{id}` caches it; a bare order would blank the page after every write.
- **`TextArea` cuts nothing.** As the GOV.UK character count, it has no native `maxlength`, which
  truncates a paste silently; it shows the characters over the limit and the form's schema refuses
  them. The count is announced only after typing pauses, never after a reset.
- **The entry schema measures texts in NFC**, refuses lone surrogates and reports an own weapon
  chosen as a loan with `ownWeapon`, as the server does.

### Review notes (group 10)

- **Label in name.** "Prepare order" buttons are named "Prepare order of <comparsa>", so the
  visible text starts the accessible name (WCAG 2.5.3).
- **Busy rows, not disabled pages.** "Prepare order" and "Add" show `pending` on the clicked row and
  ignore other clicks meanwhile; a refused preparation refetches the overview, and a refused add
  (including `orders.alreadyInEdition`) reloads the order.
- **Redirect only from "Orders".** A FiringChief with one prepared order is taken to it from
  `/orders` only, so the order's back link to the edition's overview never loops.
- **Phones.** Stacked entries label each value ("Flask: Own") with a `dl`; the entries have an `h2`.
- **Stable columns.** The entries table keeps its column set while the order is refetched (the edit
  action reads the order from the cache), so an open entry panel is never remounted.

### Review notes (group 11)

- **The lookup belongs to its DNI/NIE.** The panel keeps the last lookup, so choosing another weapon
  source and coming back keeps it; editing the DNI/NIE forgets it and the chosen weapon, and an
  answer that arrives after the DNI/NIE changed is dropped. Enter in the DNI/NIE runs the lookup.
- **Errors where the person acts.** A loan without a lookup is reported at the DNI/NIE ("Look up the
  owner first"), never on a field that is not on screen; errors of a weapon source vanish when
  another source is chosen.
- **Announcements.** The lookup's progress and outcome go to a polite live region that is always
  present; after "Change the owner" focus moves to the DNI/NIE; a panel removed because the orders
  closed is announced on the page once it is gone.
- **Version with the values.** The entry's version is a form value, set when the panel opens or is
  reset after a conflict, so a refetch while editing cannot pair new values with an old version.
- **Personal data in memory.** The lookup and entry writes use `gcTime: 0`, so their payloads leave
  the mutation cache once settled.

### Review notes (group 12)

- **Nothing changes under an open dialog.** Every move changes the status and so removes the action
  that started it. The new order is cached and announced only after the dialog closes
  (`onConfirmed`), and a refusal that makes the order out of date reloads it when the person closes
  the dialog that explains it. The message is never lost with the dialog, and focus lands on the
  page's notice.
- **Dialogs scroll.** `AlertDialogContent` is capped at the viewport height and scrolls, so a long
  list of warnings never pushes "Submit" out of reach (WCAG 1.4.10); the warnings list itself does
  not scroll on its own.
- **Return reason.** The field takes focus when the dialog opens and again when it is confirmed
  empty, so its error is read; the error clears as the person types.

### Review notes (group 13)

- **Consequences are read first.** `ConfirmDialog` gained `notes`: the deletion's effect on the orders
  is shown before the generic text and is part of the dialog's accessible description, so it is
  heard before "Delete" can be reached. The arquebusier is refetched when the dialog opens, so a
  closing of the orders meanwhile changes the warning to "kept as history".
- **Wording.** Lent weapons are described as the orders show them: "no longer in the registry".

### Verification (task 14.3)

- **Results.** Backend: 1702 tests, 96.6 % line coverage of `PolvorApp.ComparsaOrders` and 100 % of
  its contracts (96 % overall); the one failure seen under load, `PhotoRaceTests`, passes alone and
  is outside this change. Frontend: lint, types, formatting, i18n and the build pass; 2013 Vitest
  tests. Playwright: the full suite passes twice in a row (260 tests). Security grep: no route
  without a session (reads answer 401; writes are refused earlier by the antiforgery check), no
  DNI/NIE in routes, logs or audit data.
- **Fixed from `pr-test-analyzer` and `e2e-runner`.** A route guard test for `/api/comparsa-orders`,
  the borrower's deletion removing the external owner's loan, audit content (actor, comparsa,
  previous and current status, `byAdmin`), no audit on refusals, the order-writes rate limit; the
  `serial-state` project runs one file at a time, and the review cycle puts the seeded entries and
  the attested submission back.
- **Follow-ups (not blocking).**
  - HTTP-level races for an entry write, a submission and a preparation against a concurrent
    closing (the directory and the deletion paths have them).
  - An Admin's edit of a `SUBMITTED` order keeping the status, and a FiringChief's added entry
    sending a submitted order back to draft, over HTTP.
  - Concurrent adds of one arquebusier to two orders, and two saves with the same version.
  - Preparing an order in E2E: seeded orders cannot be deleted, so it would change the seed for
    good; a disposable edition would be needed.
  - The 800-entry overview test measures wall-clock time (accepted in group 6).

### History identifiers on the order page (after verification)

- **Spec, not a reword.** The order page showed only the name and the ID Unión, while three scenarios
  ("Name kept after deletion", "Past entries keep the history", "Owned weapon kept in the history")
  also ask for the DNI/NIE and a removed weapon's ownership guide. The maintainer chose to show them.
- **What the API returns.** Each entry's `arquebusier` carries `nationalId`: the registry's while the
  arquebusier is in it, else the entry's copy. `ownedWeapon` is now its own shape with
  `ownershipGuideNumber`, filled from the copy only once the weapon was removed; a weapon still in the
  registry and the `ownedWeapons` choices carry no guide, as before.
- **On screen.** Under the name: "DNI/NIE … · ID Unión …", then "First year" or "No longer in the
  registry"; a removed weapon reads "Own: model number, guide … (no longer in the registry)". Both
  stay within BR-12 (the order's comparsa and the Admin) and blank with the copy on erasure (#15).
