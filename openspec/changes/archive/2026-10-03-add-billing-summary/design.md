# Design

## Context

See `proposal.md` for the motivation and `specs/billing/spec.md` for the behaviour. The facts
below come from the current code and shape the approach.

- **Module anatomy** (`backend/src/Modules/README.md`, ADR-0001): one implementation project and
  one `.Contracts` project per capability. A `.Contracts` project references only
  `PolvorApp.SharedKernel`, so one module's contracts cannot use another's types. The
  architecture tests discover every project under `src/`; `ModuleRegistrationTests` checks the
  `AddModules(...)` call in `Program.cs`.
- **A module without data** already exists: `ComplianceInsights` owns rules behind
  `IComplianceRules`, has no schema, and the registry calls it to attach warnings to its own
  responses ("Contracts in both directions").
- **Editions contract.** `IEditionDirectory` returns `EditionSnapshot(Id, Year, Status,
  OrdersOpen, FestivalStartsOn, FestivalEndsOn, OfferedWeaponModelIds)`, built in
  `EditionDirectory` from the entity or from the `FOR SHARE` locking read (`EditionRow`). The
  prices are four nullable `decimal` columns of `FestivalEdition` (`numeric(6,2)`), optional
  while `DRAFT`. The "Money" convention: `decimal` in C#, JSON numbers with two decimals, never
  rounded.
- **Orders.** `OrderTotals.Of(entries, withWarnings)` derives the per-order quantities, and
  `OrderTotals.Sum` adds them up; `OrderViews` builds `OrderResponse` (with `Totals`) and
  `OrderOverview` builds `OverviewResponse` (rows with `Totals`, and the edition totals for
  Admins only). Both already apply the comparsa scope (BR-12), the `DRAFT` rule for FiringChiefs
  and the lender exception. Every write endpoint answers with the refreshed `OrderResponse`, so
  the page re-renders from it.
- **Frontend.** `features/comparsa-orders` already imports from other features
  (`compliance-insights`). `useFormatters().currency` formats euros per language. `KeyFacts`,
  `SectionCard`, `DataTable` and `Breakdown` exist; none renders money lines with a total row
  (`DataTable` has no footer, `Breakdown` holds counts only). `StatusBadge` kinds live in
  `components/app/status.ts`, labels in `ui:status.<kind>.<VALUE>`.

## Goals / Non-Goals

**Goals:**
- One place that knows how an order is priced, so the order page, the overview and the later
  exports (#12) cannot disagree.
- No new endpoint and no second request: the billing travels with the order data it is derived
  from, read in the same query, under the same scope.

**Non-Goals:**
- Any storage: no table, no migration, no cache of amounts.
- A billing endpoint of its own. If `add-exports` needs billing outside the orders, it calls the
  same calculator.

## Decisions

### D1. A `Billing` module without data, called by the orders

`Modules/Billing` (`PolvorApp.Billing`, `PolvorApp.Billing.Contracts`) owns the pricing rule.
`Billing.Contracts` exposes:

```csharp
public interface IBillingCalculator
{
    BillingSummary Summarise(BillingQuantities quantities, BillingPrices prices, BillingState state);
}
public sealed record BillingQuantities(int PowderKg, int CapsBoxes, int WeaponRentals, int FlaskRentals);
public sealed record BillingPrices(decimal? PowderPerKg, decimal? CapsBox, decimal? WeaponRental, decimal? FlaskRental);
public sealed record BillingLine(BillingConcept Concept, int Quantity, decimal? UnitPrice, decimal? Amount);
public sealed record BillingSummary(IReadOnlyList<BillingLine> Lines, decimal? Total, BillingState State, IReadOnlyList<BillingConcept> MissingPrices);
```

`BillingConcept` (`POWDER`, `CAPS`, `WEAPON_RENTAL`, `FLASK_RENTAL`) and `BillingState`
(`PROVISIONAL`, `FINAL`) are coded enums (`[JsonStringEnumMemberName]`, `CodeEnumConverter`), so
`CodedEnumsTests` covers them. `MissingPrices` holds the concepts whose price is not set, in line
order (each concept has one price; the UI names the price). The calculator refuses negative
quantities and writes unit prices and amounts with two decimals.

The edition billing is the same call: the orders add up their `OrderTotals` (they already have
`OrderTotals.Sum`) and price the sum once, so the edition total is the sum of the order totals (no
rounding can make it differ). The orders decide the state: an order is `FINAL` when `VALIDATED`;
the edition is `FINAL` when it has at least one prepared order and all are `VALIDATED`.

*Review note (group 2):* a first version had `Sum(IReadOnlyCollection<BillingSummary>, prices)`,
a `bool final` flag and missing prices as edition field names. The reviewers flagged that `Sum`
read hand-built summaries (`Single` could throw), ignored their prices and made an empty edition
`FINAL`; it was removed in favour of summing quantities, the flag became `BillingState`, and the
missing prices became `BillingConcept`s, so the contract does not leak the editions' field names.

`ComparsaOrders` maps its `OrderTotals` to `BillingQuantities` (powder kg; normal + small caps;
the sum of `WeaponRentals`; 1 kg + 2 kg flasks) and the edition's prices to `BillingPrices`, and
adds the result to its responses (D3). The module registers `IBillingCalculator` as a singleton;
it has no endpoints and no `DbContext`.

*Alternatives considered:*
- **Billing endpoints in the `Billing` module** (`GET /api/billing/orders/{id}`,
  `/api/billing/editions/{id}`), reading quantities through a new orders contract. Rejected: it
  repeats the scope, `DRAFT`-visibility and lender rules that `OrderViews` and `OrderOverview`
  already enforce, doubles the requests of each page, and lets the amount and the totals on the
  same page come from two different reads.
- **The rule inside `ComparsaOrders`** (`Totals/BillingSummary.cs`). Rejected: it breaks the
  one-module-per-capability convention, and `add-exports` would have to reach into orders to
  price anything.
- **A static helper in `Billing.Contracts`.** Rejected: contracts hold types, not behaviour; the
  `ComplianceInsights` precedent is an interface implemented in the module.

### D2. Prices in the edition snapshot

`FestivalEditions.Contracts` gains `EditionPrices(decimal? PowderPerKg, decimal? CapsBox,
decimal? WeaponRental, decimal? FlaskRental)`, and `EditionSnapshot` gains `Prices`.
`EditionDirectory` fills it from the entity and from the locking read (`EditionRow` selects the
four columns too), so every snapshot is complete. The orders map it to `BillingPrices` in one
place (`Totals/BillingMapping.cs` inside `ComparsaOrders`), which also maps an order status, or the statuses of an edition's orders, to the `BillingState`.

*Alternative:* a separate `GetPricesAsync(editionId)`. Rejected: one more query per page for four
columns of a row that is already read.

### D3. API (additive)

- `OrderResponse` gains `billing: BillingSummaryResponse`, computed from the same `OrderTotals`
  as `totals`, `final` when the order is `VALIDATED`.
- `OverviewRowResponse` gains `billing: BillingSummaryResponse | null` (null when not prepared).
- `OverviewResponse` gains `editionBilling: BillingSummaryResponse | null`, set for Admins only,
  next to `totals` (same rule as the edition totals).
- `BillingSummaryResponse(lines, total, state, missingPrices)`, `BillingLineResponse(concept,
  quantity, unitPrice, amount)`: amounts as JSON numbers with two decimals, `null` when the price
  is missing; `missingPrices` as concept codes (`["CAPS"]`).

Every write that returns `OrderResponse` (entry edit, add, submit, validate, return, loan) thus
returns the new billing too. `contracts/openapi.json` is regenerated by the API build and the
client by `npm run generate:api`; `OpenApiDocumentTests` guards the document.

### D4. Computation rules

- `Amount = Quantity × UnitPrice` in `decimal`; `Total = Σ Amount` only when no price is missing.
  The inputs have at most two decimals and the quantities are integers, so the results are exact;
  the response serialiser writes two decimals (the "Money" convention).
- Quantities come from `OrderTotals`, so "every entry counts as in the totals" holds by
  construction: deleted-arquebusier entries count, removed entries do not.
- `RENTAL` entries count by `WeaponRentals` (grouped by model). An entry is `RENTAL` exactly when it
  has a model (`ck_edition_entries_rental`), so the count is the number of rentals.
- Ranges: at most ~1,000 entries × 99 caps × 9,999.99 € fits `decimal` and a JS `number` to the
  cent (below 2^53 cents).

### D5. Frontend

- **`components/app/AmountTable`** (new composite, ADR-0009): a `<table>` with a `<caption>`,
  rows `{ id, label, quantity, unitPrice, amount }` already formatted by the caller, and a total
  row in `<tfoot>` (`<th scope="row">`). Below `sm`, the quantity and unit price move under the
  label as one line ("5 kg × 55,00 €") and the table keeps two columns, so a 360 px screen never
  scrolls. Story in Storybook, axe test, entry in `docs/design/patterns.md`.
- **`components/app/status.ts`**: kind `billing` with `PROVISIONAL` (info, `Clock`) and `FINAL`
  (success, `CircleCheck`); labels in `ui:status.billing.*`.
- **`features/billing/`**:
  - `components/BillingSummarySection.tsx`: a `SectionCard` "Billing summary" with the state
    badge, the provisional note, the `AmountTable`, and, when prices are missing, an
    `AlertBanner` naming them instead of the total;
  - `components/BillingAmount.tsx`: the overview cell (total as currency + state badge, or
    "price missing");
  - `billingText.ts`: concept labels and quantity texts (`{{count}} kg`, boxes, rentals,
    flasks), plural-aware.
- **`features/comparsa-orders`**: `OrderPage` renders `BillingSummarySection` after the totals;
  `OrdersOverviewPage` adds an "Amount" column (list rows and the phone `mobileRow`), and for
  Admins `EditionBilling` after `EditionTotals`.
- **i18n**: a new `billing` namespace in `es-ES`, `ca-ES-valencia` and `en`
  (`billing.section.title`, `.provisionalNote`, `.finalNote`, `.concepts.*`, `.quantity.*`,
  `.columns.*`, `.total`, `.missingPrices`, `.prices.*`, `overview.amount`, `edition.title`),
  registered in `i18n/config.ts` and the typed resources; `ui:status.billing.*`.

### D6. Security, GDPR and audit

No new endpoint, permission or personal data: a summary holds quantities and euros per comparsa.
Visibility is inherited from the order and overview queries (BR-12, the `DRAFT` rule, `404` out of
scope), and the lender exception exposes no billing because the `LentOut` part of a response
carries no totals. Reading writes nothing, so nothing is audited (SEC-05 covers writes and
exports); the price changes and entry edits that move amounts stay audited by their modules.

## Risks / Trade-offs

- **[A price edited after a comparsa paid changes its "final" amount]** → Accepted (maintainer
  decision: always live). Price edits are audited with old and new values, and the final badge
  only says the Federation validated the order. If freezing is needed later, the calculator's
  inputs make it a local change (store `BillingPrices` at validation).
- **[Order and overview responses grow]** → Four lines per row and ~20 rows: negligible.
- **[`EditionSnapshot` grows for every consumer]** → It is a record; existing callers ignore the
  new property. Tests that build snapshots by hand get a default.
- **[Edition total differs from the sum of rounded row totals]** → Cannot happen: exact
  `decimal`, and the edition prices its summed quantities once, which equals the sum of the amounts.
- **[Long concept labels in Valencian on a phone]** → `AmountTable` stacks quantity and price
  under the label below `sm`; covered by a story with the longest locale.

## Migration Plan

No database migration. Deploy is the usual API + web image update; the API change is additive,
so an older web build keeps working until it is replaced. Rollback is redeploying the previous
images.

### Research notes (task 1.1)

- **Decimals in JSON** (System.Text.Json, .NET 10): it writes a `decimal` with its scale, so the
  editions already answer two decimals with `decimal.Round(value, 2) + 0.00m`
  (`EditionViews.Money`). The billing responses use the same helper idea: quantity × price keeps
  scale 2, and `+ 0.00m` makes `0` read `0.00`. Nullable `decimal` in nested records serialises
  the same way.
- **Currency** (`Intl.NumberFormat`, Node 24 / evergreen browsers; `useFormatters().currency`
  maps `en` to `en-GB`): `es-ES` "360,50 €", "0,30 €", "12.345,50 €" but "1234,50 €" (no
  grouping below five digits); `ca-ES-valencia` "360,50 €", "1.234,50 €"; `en` "€360.50",
  "€1,234.50". Tests assert these strings, with a non-breaking space before "€" in es and ca
  (match with a regex on `\s`).
- **Accessible tables**: `<caption>` names the table; the total row goes in `<tfoot>` with its
  label as `<th scope="row">`; the shadcn/ui `table` primitive in the repo already exports
  `TableFooter` and `TableCaption`.
- **Plurals** (i18next 26.4, react-i18next 17): `key_one` / `key_other` resolved through
  `Intl.PluralRules` with `{ count }`; the repo uses only `_one`/`_other` in the three locales,
  and `_zero` is optional. The quantity texts follow that.
- **Name clash found**: `FestivalEditions` already has an internal `EditionPrices` record
  (`Editions/EditionInput.cs`) with the same four nullable prices. D2 moves it to
  `FestivalEditions.Contracts` as the public `EditionPrices` (keeping `None`), and the
  `Of(FestivalEdition)` factory stays in the module as an internal extension, so there is one
  type, not two with the same name.

### Review notes (groups 2–4)

- **Group 2** (`csharp-reviewer`, `type-design-analyzer`): no CRITICAL/HIGH. Applied: `Sum`
  dropped (see D1), `BillingState` instead of a `bool`, missing prices as `BillingConcept`s, unit
  prices written with two decimals, negative quantities refused, and amount assertions compared as
  invariant text so a lost scale fails. Kept: plain records without computed properties (the
  calculator is the only producer, and its tests assert the invariants).
- **Group 3** (`csharp-reviewer`): approved. Applied: a locking-read test for an unpriced draft,
  shared `EditionData.CompletePrices`, and price columns read by name instead of ordinal.
- **Group 4** (`csharp-reviewer`, `security-reviewer`, `silent-failure-hunter`): no
  CRITICAL/HIGH; BR-12 holds (billing is built only after the scope and draft checks, `LentOut`
  carries no amount, `editionBilling` is null for FiringChiefs). Applied: `StateOfOrder` /
  `StateOfEdition`, the total summed with `Amount!.Value` so a missing price can never read as
  zero, and tests for rentals of several models, an order with nothing charged, missing prices in
  the overview rows and edition billing, and a FiringChief on a draft edition. Not added: a
  "lender's comparsa" API test, because `LentOutResponse` has no amount field to leak (checked by
  the security review); `checked` arithmetic, because D4's ranges are far below `int` limits.
- **Group 5** (`react-reviewer`, `typescript-reviewer`, `a11y-architect`): no CRITICAL/HIGH.
  Applied: a missing price reads "No price" visibly (not a dash with screen-reader text only), the
  row header wraps long words and separates the label from the stacked "quantity × price",
  `AmountTable` tests query by role instead of Tailwind classes (which jsdom cannot apply; the
  360 px layout is checked end to end), a typed `BillingText` interface, test data that never
  sums a null amount, and a Valencian missing-price test. The missing-price sentence names the
  prices without articles ("caja de pistones"), because joining "de" + "el" read wrong. Kept: no
  focusable scroll region (the table must never scroll; E2E checks 360 px), and the key parity
  across languages is already enforced by `npm run check-i18n`.
- **Group 6** (`react-reviewer`, `a11y-architect`): no CRITICAL/HIGH. Applied: the edition billing
  has its own "no total" sentence, the phone row labels the amount ("Amount:") and aligns it to
  the start, and tests for the edition scope with a missing price, a missing price in the overview
  and the exact "No price" cells. Kept: the missing-price banner is not a live region (a lasting
  state shown with the page, after the table), and a not-prepared row's amount cell stays empty
  ("shows no amount").

### Verification (task 7.3)

`verification-loop`: **PASS**.

- **Backend**: build and `dotnet format --verify-no-changes` clean; 1,755 tests pass (architecture,
  coded enums, OpenAPI document, Testcontainers). The `Billing` module, `BillingMapping`,
  `OrderViews`, `OrderOverview`, `EditionDirectory` and `FestivalEdition` are at 100 % line
  coverage; `contracts/openapi.json` changed additively only.
- **Frontend**: build, `tsc`, ESLint (0 warnings), Prettier and `check-i18n` clean; 2,070 tests
  pass, 95 % line coverage overall, the new billing files at 100 % and `OrdersOverviewPage` at
  98.5 %. `docs/design/status.md` was regenerated with `npm run docs:design`.
- **E2E** (rebuilt compose stack, seeded): 260 passed twice in a row (14 skips are the existing
  engine and mobile skips).
- **Security grep**: no new route, no logging of amounts, no personal data in the billing types or
  responses, no secrets or console output.
- **`pr-test-analyzer`**: every spec scenario is covered. Thin spots kept as they are: the
  lender's-comparsa case (rated 5/10: `LentOutResponse` has no amount and a `LOAN` entry is never
  charged, so there is nothing to leak), price and Admin edits tested through direct updates
  rather than their endpoints (billing is computed per read, so the path does not matter), and
  "returned after validation" checked through the status mapping and the E2E cycle.
- **`e2e-runner`**: no CRITICAL. Applied its HIGH: the 360 px check also asserts that the billing
  table's own scroll container does not overflow (a too-wide table would scroll inside it without
  widening the page) and uses `clientWidth`; "Total" is matched exactly.

Follow-ups: none for this change. Local note: the machine's Node is 24.14 while `package.json`
asks for ≥24.15 (no failure seen); Docker builds must be started from PowerShell, because Git
Bash lacks the Docker credential helper on its PATH.
