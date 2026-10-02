# Proposal

## Why

Every yearly order, loan, rental, distribution and billing figure belongs to one festival edition
(`docs/data-model.md` §2, "Edition-scoped"). Today the Federation runs the year from a calendar
sheet, a price written in each comparsa's workbook (`docs/current-state.md` §2) and a Google Form
it opens and closes by hand (§6). PolvorApp has the registry, the catalogue and the compliance
warnings (#4–#8). Comparsa orders (#10) need an edition to attach to, with its prices, its
rentable models and its order window. So the edition comes next. The registry lock (BR-10) was
left for this change by #8.

Capability (from `docs/mvp.md`): **`festival-editions`**, change #9 of the sequence. It implements:
- **UC-10**: create and configure an edition (festival dates, order window, available rental
  models, prices, calendar milestones);
- **UC-11**: start and close the edition, open and close its orders, and lock or unlock the
  registry.

It enforces these rules:
- **BR-07**: only active, rentable catalogue models can be offered. Pistols are never rentable.
- **BR-10**: FiringChiefs may edit orders only while the orders of the current edition are open.
  The registry lock makes the registry read-only for FiringChiefs. Admins can always edit.
- **BR-12**: FiringChiefs see editions read-only, and never drafts.

It follows these ADRs:
- ADR-0001: a new `FestivalEditions` module with its own `editions` schema;
- ADR-0002;
- ADR-0007: three locales;
- ADR-0009 and ADR-0011: composites and tokens only.

## What Changes

- **Festival editions (UC-10)**, managed by Admins:
  - An edition has a `year`, unique and fixed once created, and the festival dates within that year.
  - It has the planned order window: the dates the orders open and close. They are published
    deadlines that FiringChiefs read and the later reminders (#14) will use. They never open or
    close the orders by themselves (maintainer decision).
  - It has flat prices in euros (`EditionPrices`: `powderPerKg`, `capsBox`, `weaponRental`,
    `flaskRental`).
  - It has the rental models offered in it (`EditionWeaponModel`).
  - A new edition starts with the prices and the still-rentable models of the latest earlier
    edition, all editable. Its dates and milestones start empty (maintainer decision).
  - Only a `DRAFT` edition can be deleted.
- **Calendar milestones**: dated, titled entries of an edition (license renewal call, course,
  deadlines…), added, edited and removed by Admins.
- **Edition lifecycle (UC-11)**: Admins move an edition by hand, one step at a time, from `DRAFT`
  (in preparation, hidden from FiringChiefs) to `IN_PROGRESS` to `CLOSED`. They can also move it
  one step back to reopen. Each move is confirmed and audited. These rules are blocking:
  - At most one edition is `IN_PROGRESS` at a time. It is the **current edition** (maintainer
    decision). It stays current after its orders close, through distribution, billing and
    exports, until the Admin closes it.
  - Starting an edition needs the festival dates, the order window dates and the prices.
  - An edition leaves `IN_PROGRESS` only with its orders closed.
- **Orders open / closed (UC-11, BR-10)**: on the edition in progress, Admins open and close the
  orders by hand, as often as needed (maintainer decision: this replaces separate order,
  correction and lock stages). Reopening after a review is the corrections window. Each change is
  confirmed and audited. This change also defines the contract that #10 uses: FiringChiefs may
  edit orders only while the orders of the current edition are open.
- **Registry lock (UC-11, BR-10)**: a lock of its own, independent of the editions (maintainer
  decision). While it is on, FiringChiefs cannot change the registry: they cannot register, edit,
  change the status, manage photos or owned weapons, or delete. These writes are refused with
  `409`. Admins keep every write, including transfers and the import. Locking and unlocking are
  audited. FiringChiefs see a notice in the registry and no edit actions.
- **Screens**:
  - an "Editions" page (list), an edition detail page in read mode with section editing (dates,
    order window, prices, rental models, milestones), the lifecycle and orders actions, and a
    create form;
  - a current-edition card on the start page: orders open or closed, next deadline, upcoming
    milestones;
  - the registry lock action and notice on the Arquebusiers page.

  FiringChiefs get the same pages read-only, without drafts. Everything is translated into the
  three locales and works on a phone.
- **Synthetic seed**: a closed past edition, a current edition in progress with open orders and
  milestones, and a draft for next year.

## Non-goals

- **Comparsa orders, entries, loans and their editing rules** (#10). This change only exposes
  whether the current edition allows FiringChief edits.
- **Automatic opening or closing of the orders by date**, and blocking edits outside the window
  dates.
- **Separate order, correction and lock stages** (`ORDERS_OPEN`, `CORRECTIONS_OPEN`, `LOCKED` in
  the earlier data model). Orders are simply open or closed (maintainer decision).
- **Email reminders for milestones or deadlines.** The `notify` flag of `CalendarMilestone` is
  added by `add-notifications` (#14), which sends them.
- **Billing**: prices are stored and shown here; amounts owed arrive in #11.
- **Distribution days, slots and rental weapon units** (`RentalWeapon`, #13).
- **Quantities or stock** of rental models. Only which models are offered is recorded.
- **Several editions in progress at once**, and per-comparsa prices or windows.
- **Copying milestones or dates** from the previous edition.
- **Scheduled registry locks**, and per-comparsa locks.

## Capabilities

### New Capabilities
- `festival-editions`: festival editions, their order window, prices, available rental models
  and calendar milestones, the edition lifecycle, opening and closing the orders, the current
  edition, edition visibility
  for FiringChiefs, the editions screens and the current-edition card, audit and synthetic data.

### Modified Capabilities
- `arquebusier-registry`: a new "Registry lock" requirement (BR-10, UC-11). It makes every
  FiringChief registry write conditional on the lock being off. It also adds the lock screens
  and audit. The existing requirements keep their rules. FiringChief writes gain one refusal
  reason, `registry.locked`.

The `federation-catalog` requirements do not change. "Deleting comparsas and weapon models"
already names edition availability as a reference that blocks deletion. This change implements
that reference through the catalogue's usage contract.

## Impact

- **Backend**:
  - A new module, `Modules/FestivalEditions` (`PolvorApp.FestivalEditions` and `.Contracts`).
    - Its `editions` schema has `festival_editions`, `edition_weapon_models` and
      `calendar_milestones`, with migrations ordered after the registry.
    - It implements `ICatalogUsage` for weapon models.
    - Its Contracts expose the current edition and its order-editing state to #10.
  - The registry module gains the registry lock: one settings row in `registry`, a check inside
    `RegistryWriteGuard` for FiringChief writes, and two endpoints.
  - Registration in `Program.cs`, the solution, the `Dockerfile` and `ModelDriftTests`.
- **API**:
  - `/api/editions`: list, current, detail, create, update, status, orders, rental models and
    milestones;
  - `/api/registry/lock`;
  - a new problem code `registry.locked` on the FiringChief registry writes.

  `contracts/openapi.json` and the orval client are regenerated. The change is additive.
- **Frontend**:
  - a new `features/festival-editions` folder with its routes and an "Editions" navigation entry
    for both roles;
  - a new `MoneyInput` composite in `components/app/`, with its story and axe test;
  - the `edition` status mapping in `components/app/status.ts` reduced to `DRAFT`, `IN_PROGRESS`
    and `CLOSED`, plus a new `orders` mapping (`OPEN`, `CLOSED`);
  - the current-edition card on `DashboardPage`;
  - the lock action and notice in `features/arquebusier-registry`;
  - a new `editions` i18n namespace and new `registry:lock.*` keys in the three locales.
- **Docs**:
  - `docs/data-model.md` (edition fields, statuses `DRAFT` / `IN_PROGRESS` / `CLOSED` and the
    orders flag replacing the five statuses, BR-10 wording, registry lock);
  - `docs/use-cases.md` (UC-10 and UC-11 notes);
  - `docs/glossary.md` (`EditionStatus`, orders open, current edition, registry lock);
  - `docs/open-questions.md` (Q-25 note: corrections = reopening the orders);
  - `docs/compliance.md` (SEC-05);
  - `docs/development.md` (seed);
  - `backend/src/Modules/README.md`;
  - `docs/design/` (`MoneyInput`, the edition actions pattern, the `edition` and `orders`
    statuses in `status.md`);
  - `docs/mvp.md` (status of #9).
- **Security and GDPR**:
  - Editions hold no personal data.
  - Every write is Admin-only, audited in the same transaction, and checked on the server. That
    includes the registry lock, which is enforced on the server for every FiringChief write.
  - Drafts are invisible to FiringChiefs (`404`).
- **ADRs**: none new.
