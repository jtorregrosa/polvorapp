# Proposal

## Why

Each arquebusier pays their fee to the comparsa, and the comparsa makes one joint transfer to the
Federation for its whole order (`docs/current-state.md` §2; Q-31, Q-35). Today the FiringChief
works out that amount in the order spreadsheet (powder price per kg in its header block), and the
Federation checks it by hand. PolvorApp now has the edition prices (#9) and the comparsa orders
with their entries and totals (#10), so it can tell both sides what each comparsa owes, from the
same data, without a spreadsheet.

Capability (from `docs/mvp.md`): **`billing`**, change #11 of the sequence. It implements:
- **UC-28**: the comparsa billing summary, the amount owed to the Federation for an edition
  (powder + caps + rentals), for FiringChiefs (their comparsas) and Admins (every comparsa and
  the edition total).

It relies on these rules:
- **BR-05**: a `RESERVE` entry has no powder, caps, weapon or flask, so it is never charged.
- **BR-12**: FiringChiefs see the billing summary of their comparsas only. It travels with the
  order and the orders overview, which already enforce the scope.
- The four flat `EditionPrices` of `festival-editions` (Q-35): `powderPerKg`, `capsBox`,
  `weaponRental`, `flaskRental`. Caps are one price whatever their type, and a flask rental one
  price whatever its size.

It follows these ADRs: ADR-0001 (a new `Billing` module, without data), ADR-0002, ADR-0007,
ADR-0009 and ADR-0011.

## What Changes

- **Billing summary (UC-28)**, `BillingSummary`, for every prepared order:
  - four lines, each with its quantity, unit price and amount: powder (kg × `powderPerKg`), caps
    (boxes of both types × `capsBox`), weapon rentals (`RENTAL` entries × `weaponRental`) and
    flask rentals (1 kg and 2 kg × `flaskRental`), and their total;
  - owned weapons, loans and owned flasks are never charged;
  - **always derived** from the order's current entries and the edition's current prices, and
    never stored (maintainer decision). A price change, already audited by the editions, changes
    every summary of that edition;
  - **provisional** until the order is `VALIDATED`, **final** afterwards (maintainer decision). A
    returned order becomes provisional again;
  - amounts exact in euros with two decimals, never rounded through floating point;
  - per concept only, not per arquebusier (maintainer decision).
- **Edition billing (Admins)**: the sum of the summaries of every prepared order of the edition,
  provisional while any of them is not `VALIDATED`.
- **Screens** (maintainer decision: inside the orders, no download):
  - the order page gains a "Billing summary" section with the four lines, the total and whether
    it is provisional or final;
  - the orders overview gains the amount of each prepared order and, for Admins, the edition's
    billing next to its totals;
  - a summary whose edition lacks a price (an edition moved back to `DRAFT`) says which price is
    missing instead of a total.
- **No audit entries**: billing is read-only and is not an export (the downloads stay in
  `add-exports`, #12).
- **Synthetic data**: the seeded orders already cover every concept; the seed gains no data.

## Non-goals

- Payments, receipts, invoices or tracking what each comparsa has paid (out of scope in
  `docs/use-cases.md`).
- What each arquebusier owes the comparsa: comparsa-internal (maintainer decision).
- Freezing the prices of a validated order (maintainer decision: always live).
- Different prices per caps type, flask size or weapon model (Q-35: flat prices).
- PDF or spreadsheet downloads of the summary (`add-exports`, #12).
- Discounts, surcharges or manual adjustments.

## Capabilities

### New Capabilities
- `billing`: the billing summary of an order (lines, total, provisional or final, missing
  prices), the edition billing for Admins, visibility, and where the screens show them.

### Modified Capabilities
None. The order page and the overview gain a billing section and an amount column, specified in
`billing`. The `comparsa-orders` and `festival-editions` requirements do not change: their totals,
visibility and prices are used as they are.

## Impact

- **Backend**:
  - A new module, `Modules/Billing` (`PolvorApp.Billing` and `.Contracts`), without data, schema
    or migrations, like `ComplianceInsights`. It owns the pricing rule behind
    `Billing.Contracts.IBillingCalculator`.
  - `FestivalEditions.Contracts.EditionSnapshot` gains the edition's `EditionPrices`.
  - `ComparsaOrders` calls the calculator and adds the billing to the order response, to each
    overview row and, for Admins, to the overview; the OpenAPI document and the generated client
    change accordingly (additive).
  - Architecture tests, solution, `Program.cs` and the `Dockerfile` restore layer list the new
    module.
- **Frontend**: a new `features/billing` folder with the billing section and the amount cell,
  used by the orders pages; a new `components/app` composite for a table of amounts with a total
  row (story and axe test); a `billing` status badge (provisional, final); a `billing` namespace
  in the three locales.
- **Docs**: `docs/data-model.md` (`BillingSummary`), `docs/use-cases.md` (UC-28 notes),
  `docs/design/patterns.md` (the new composite), `backend/src/Modules/README.md`, and
  `docs/mvp.md` on archive.
- **Security and GDPR**: no new endpoint, no personal data (amounts per comparsa), no new
  permission; the scope is the one the orders already enforce.
