# Proposal

## Why

The yearly order is the reason the Federation and the comparsas exchange spreadsheets today. Each
FiringChief keeps an order sheet per year, re-types it into a Google Form opened by the
Federation, one submission per arquebusier (`docs/current-state.md` §2, §6; Q-24). The Federation
then validates and aggregates twenty of them by hand. PolvorApp now has the registry, the
catalogue, the compliance warnings and the festival editions with their prices, rentable models
and the `ordersOpen` flag (#4–#9). The comparsa order is the next step, and billing (#11), exports
(#12) and distribution (#13) all build on its entries.

Capability (from `docs/mvp.md`): **`comparsa-orders`**, change #10 of the sequence. It implements:
- **UC-12**: prepare the comparsa order, one entry per arquebusier, pre-filled from the previous
  edition;
- **UC-13**: register a weapon loan, from an arquebusier of any comparsa or from an external
  owner;
- **UC-14**: submit the order with an attestation, showing the pending warnings, and edit it
  again while the orders are open;
- **UC-15**: review the orders, validate them or return them with a reason, and edit any order
  (Admins, exceptional cases);
- **UC-16**: the Federation dashboard, with the status of each comparsa's order and the totals;
- the "first year" flag and statistic of **UC-07**, deferred from #7 because it needs previous
  entries.

It enforces these rules:
- **BR-04**: compliance warnings of `ACTIVE` entries, evaluated on the festival dates. They are
  warnings only; the FiringChief attests on submission.
- **BR-05**: `powderKg` is 0, 1 or 2. A `RESERVE` entry has no powder, caps, weapon or flask.
- **BR-07**: a rental model must be offered in the edition.
- **BR-09** (amended, maintainer decision): a weapon loan comes from an owned weapon of an
  arquebusier of any comparsa, **or from an external owner** who is not in PolvorApp. No limit.
- **BR-10**: FiringChiefs edit orders only while the orders of the current edition are open;
  Admins always.
- **BR-11**: nothing is carried over between editions. The pre-fill copies choices, never powder.
- **BR-12**: FiringChiefs see and edit only their comparsas' orders. A loan shows the borrower to
  the lender's FiringChiefs and the lender to the borrower's.
- **BR-13**: an entry stays in the order of the comparsa the arquebusier belonged to when it was
  added.
- **BR-14** (amended, maintainer decision): deleting an arquebusier from the registry while the
  orders of the edition in progress are open removes their entry in that edition, after a
  confirmation that says so. Every other entry is history: past editions, and the edition in
  progress once its orders are closed. It is kept with the identity and weapon data it had, and is
  anonymised only on a GDPR erasure request (UC-26, #15).

BR-08 (one rental weapon per entry) applies when units are assigned at distribution (#13). It
follows these ADRs: ADR-0001 (a new `ComparsaOrders` module with an `orders` schema), ADR-0002,
ADR-0007, ADR-0009 and ADR-0011.

## What Changes

- **Comparsa orders (UC-12)**: one `ComparsaOrder` per comparsa and edition.
  - A FiringChief prepares it while the orders are open, and an Admin at any time once the
    edition has started.
  - Preparing creates one `EditionEntry` for every arquebusier of the comparsa (maintainer
    decision).
  - Arquebusiers registered or transferred in later are listed as "not in the order", with an
    action to add them. Nothing changes an order by itself.
  - An entry has the arquebusier's status for this edition (`ACTIVE` or `RESERVE`), `powderKg`,
    caps (`capsBoxes`, `capsType`), a `weaponSource` (`OWNED`, `RENTAL`, `LOAN`, `NONE`) with its
    owned weapon, rental model or loan, and a `flask` (`OWNED`, `RENTAL_1KG`, `RENTAL_2KG`,
    `NONE`).
  - The entry status starts as the registry status, and changing it only affects the edition
    (maintainer decision).
  - An `ACTIVE` entry may have no powder, or no weapon. Some arquebusiers only carry powder,
    others only fire, such as the comparsa captains (maintainer decision). This is never flagged.
  - Entries of past editions are history and are never removed. Each entry keeps the
    arquebusier's identity and the weapon data as they were when it was last saved, submitted or
    validated. So it still reads correctly after the arquebusier or the weapon leaves the
    registry.
- **Pre-fill (UC-12)**: each entry copies the arquebusier's previous entry: powder, caps, flask
  and weapon source. It keeps an owned weapon only if the arquebusier still owns it, and a rental
  model only if it is still offered. Loans are never copied.
- **Weapon loans (UC-13)**: the borrower's FiringChief chooses "Loan" on the entry and types the
  owner's DNI/NIE (maintainer decision).
  - When the owner is a registered arquebusier of any comparsa, the FiringChief chooses one of
    their owned weapons.
  - Otherwise they enter the external owner's name, surnames and DNI/NIE and the weapon's model,
    number and ownership guide (maintainer decision).
  - The lender's FiringChiefs see the loan, with the borrower's name and comparsa.
  - The loan keeps the lender's identity and the weapon's data, like the entries.
- **Submission (UC-14)**: a FiringChief submits a `DRAFT` or `RETURNED` order while the orders
  are open.
  - Submitting needs the attestation that the arquebusiers meet the requirements. The submit
    dialog shows the pending compliance warnings.
  - Entries that break a data rule block the submission, for example an owned weapon that was
    removed from the registry or a rental model no longer offered.
  - Editing a `SUBMITTED` order sends it back to `DRAFT`, to be submitted again. A `VALIDATED`
    order is read-only for FiringChiefs (maintainer decision).
  - An Admin can also submit an order on the comparsa's behalf, at any time, without the
    attestation. The order records that an Admin submitted it (maintainer decision).
- **Review (UC-15)**: an Admin validates an order that is `SUBMITTED`, or, to close an order the
  comparsa never submitted, one that is `DRAFT` or `RETURNED` (maintainer decision). An Admin
  returns a submitted or validated order with a reason. Admins edit any order, in any status,
  without changing its status (BR-10).
- **Compliance on the festival dates (BR-04)**: `ACTIVE` entries of the edition in progress show
  the warnings with the license checked through `festivalEndsOn` and the rest on
  `festivalStartsOn`. `LICENSE_EXPIRING` is not used for entries (maintainer decision).
- **Dashboard (UC-16)**: for Admins, every comparsa's order status ("not prepared" included) with
  its totals. The totals cover active and reserve entries, powder, caps by type, weapon rentals by
  model, flask rentals by size, loans and entries with warnings, plus the Federation totals.
  FiringChiefs see the same totals for their own orders.
- **First year (UC-07)**: an arquebusier is in their first year when they have no `ACTIVE` entry
  in an earlier edition. The flag is unknown, and not shown, until an earlier edition has orders
  (maintainer decision). It is shown on entries and on the arquebusier detail, and counted, by
  gender, in the statistics.
- **Registry effects**:
  - While the orders are open, deleting an arquebusier removes their entry in the edition in
    progress, and its loan. The delete confirmation first says which order loses the entry, and
    which of their weapons are lent in that edition (maintainer decision).
  - Once the orders are closed, and in past editions, their entries are kept with their saved
    identity, marked as "no longer in the registry" (BR-14 amended). Admins can still edit them, as
    any order (BR-10).
  - Removing an owned weapon keeps its saved data on the entries and loans that used it. While
    the arquebusier is still in the registry, those entries are marked "weapon removed" until they
    are changed.
  - A transfer leaves the current entry in the previous comparsa's order (BR-13).
  - An edition with orders cannot be deleted.
- **Screens**: an "Orders" item in the navigation for both roles. It holds the orders overview
  (the Admin dashboard), the order page with its entries, the "not in the order" list, the loans
  lent out and the submit, validate and return actions, and the entry edit panel with the loan
  lookup. Each non-draft edition links to its orders. Everything is in three locales and works on
  a phone.
- **Synthetic seed**: orders of the closed past edition and of the current edition in several
  statuses, with a loan between comparsas and a loan from an external owner.

## Non-goals

- **Billing amounts** (#11). Prices stay on the edition; the totals here are quantities.
- **Exports** for the supplier, the rental company or the Arms Authority (#12).
- **Rental weapon units and flask numbers** (`RentalWeapon`, `rentalFlaskNumber`), assigned at
  distribution (#13, UC-21), and pickup proxies (UC-19).
- **Email notifications** when an order is returned or validated (#14).
- **Anonymising entries and loans on a GDPR erasure request** (UC-26). It belongs to
  `add-audit-privacy` (#15). This change stores the identity in its own columns so #15 can erase
  it.
- **Automatic sync** of the order with the registry: new arquebusiers are added by hand, and
  status changes in the order do not touch the registry.
- **Deleting a comparsa order, or removing an entry by hand.** The only removal is the
  current-edition entry of an arquebusier deleted from the registry while the orders are open.
- **Roles inside an entry** (powder carrier, shooter, captain). An `ACTIVE` entry simply may have
  no powder or no weapon.
- **Stock or quantity limits** for rental models, and limits on loans per weapon (BR-09).
- **A "first year" filter** in the arquebusier list, and an imported start year for the history
  before PolvorApp.
- **Comparsa-internal items** of today's sheet (lunch, Salvas al Patrón, reload).

## Capabilities

### New Capabilities
- `comparsa-orders`: comparsa orders and edition entries, preparation and pre-fill, adding
  arquebusiers, entry rules, weapon loans (registered and external owners), submission with
  attestation, review, edit permissions (BR-10), visibility (BR-12), the effects of registry
  changes on entries, totals and the Federation dashboard, audit, screens and synthetic data.

### Modified Capabilities
- `compliance-insights`:
  - a new requirement for the warnings of edition entries on the festival dates;
  - a new requirement for the "first year" flag;
  - "Statistics (UC-07)" and "Statistics screen" gain the first-year counts by gender.
- `arquebusier-registry`: "Deleting an arquebusier (UC-05, BR-14)" changes in two ways.
  - While the orders are open, it removes the arquebusier's entry in the edition in progress. Its
    confirmation warns about that entry and about their weapons lent in that edition.
  - It keeps every other entry as history, with their saved identity. Only a GDPR request erases
    those (#15).
- `festival-editions`: "Edition management by Admins" refuses deleting a draft edition that has
  comparsa orders (`409`, `editions.inUse`).

The `federation-catalog` requirements do not change. "Deleting comparsas and weapon models"
already names orders as a reference that blocks deletion, and this change implements it through
`ICatalogUsage`.

## Impact

- **Backend**:
  - A new module, `Modules/ComparsaOrders` (`PolvorApp.ComparsaOrders` and `.Contracts`,
    `MigrationOrder = 50`). Its `orders` schema has `comparsa_orders`, `edition_entries` and
    `weapon_loans`.
  - Entries and loans keep a copy of the identity and weapon data, refreshed on every save,
    submission and validation.
  - Cross-schema keys go to the catalog, the registry and the editions. The keys to arquebusiers
    and owned weapons use `ON DELETE SET NULL`, a new convention, so entries outlive the registry
    records.
  - It implements `ICatalogUsage`, a new `IEditionUsage` (editions veto) and a new registry
    deletion participant. The participant removes the current-edition entry, while the orders are
    open, inside the registry's delete transaction.
  - New contracts: a per-arquebusier roster in `ArquebusierRegistry.Contracts`; the
    festival-date evaluation and the first-year rule in `ComplianceInsights.Contracts`; the
    participation history in `ComparsaOrders.Contracts`; and an edition read that shares the
    caller's transaction in `FestivalEditions.Contracts`.
  - Registration in `Program.cs`, the solution, the `Dockerfile` and `ModelDriftTests`.
- **API**:
  - `/api/comparsa-orders`: overview, prepare, detail, entries (add and edit, no delete), lender
    lookup, submit, validate and return;
  - `firstYear` on the arquebusier detail and in the statistics;
  - `editions.inUse` on the edition delete.

  `contracts/openapi.json` and the orval client are regenerated. The change is additive.
- **Frontend**:
  - a new `features/comparsa-orders` folder with its routes, an "Orders" navigation entry and an
    `orders` i18n namespace in three locales;
  - an "Orders" link on the edition detail;
  - the first-year badge on the arquebusier detail and a first-year table in the statistics.

  Existing composites cover the screens. The `order` status mapping already exists.
- **Docs**:
  - `docs/data-model.md`: `ComparsaOrder`, `EditionEntry` and `WeaponLoan` with external owners
    and saved identity, and the BR-05, BR-09 and BR-14 wording;
  - `docs/glossary.md`: loan, external owner, first year, attestation;
  - `docs/use-cases.md`: UC-12 to UC-16 notes;
  - `docs/compliance.md`: the external owners' personal data, SEC-05 and SEC-08;
  - `docs/development.md` (seed), `backend/src/Modules/README.md` (`SET NULL` keys, shared
    transactions) and `docs/design/patterns.md` (order page);
  - `docs/mvp.md`: the status of #10.
- **Security and GDPR**:
  - Orders are scoped to the comparsa (BR-12) and audited in the same transaction.
  - The lender lookup is the only cross-comparsa read. It needs a valid DNI/NIE, answers only an
    exact match with names and weapons (no ownership guide), is rate-limited and is audited.
  - External owners are new personal data (name, DNI/NIE and weapon ownership) of people who are
    not arquebusiers. They are kept with the loan, deleted when it changes, and covered by the
    later GDPR tooling (#15) and the privacy notice (Q-50).
  - Order history keeps the identity (name, DNI/NIE, federationId) and weapon data of
    arquebusiers who were deleted from the registry. This changes the retention in SEC-08 and
    BR-14. The Federation must cover it in the privacy notice, with a legal basis such as the
    records owed to the Arms Authority (Q-50). Erasure is on request (UC-26, #15).
- **ADRs**: none new.
