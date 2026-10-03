# Tasks

## 1. Research

- [x] 1.1 Check with Context7 (and `gh search code` for real-world usage) what design D3–D5 rely on:
  - System.Text.Json in ASP.NET Core 10: how the editions responses already write `decimal` with two decimals, and that a nullable `decimal` in a nested record keeps that format;
  - `Intl.NumberFormat` with `style: 'currency', currency: 'EUR'` in `es-ES`, `ca-ES-valencia` and `en` (the strings the specs and tests expect, e.g. "360,50 €" and "€360.50");
  - accessible HTML tables with `<caption>`, `<tfoot>` and `<th scope="row">`, and how the shadcn/ui `table` primitive exposes `TableFooter`;
  - react-i18next plurals for the quantity texts in the three locales.

  Record versions and any workaround in design.md. Verify: design.md updated, with no open question left.

## 2. Billing module and calculator

- [x] 2.1 Create `PolvorApp.Billing` and `PolvorApp.Billing.Contracts` (design D1): `BillingModule` without `DbContext` or endpoints, the coded enums `BillingConcept` and `BillingState`, the records `BillingQuantities`, `BillingPrices`, `BillingLine` and `BillingSummary`, and `IBillingCalculator` registered as a singleton. Register the module in `Program.cs`, `PolvorApp.slnx` and the `Dockerfile` restore layer. Verify: `dotnet build` passes, and the architecture tests, `ModuleRegistrationTests` and `CodedEnumsTests` pass.
- [x] 2.2 Write unit tests for `Summarise`, one per scenario of "Billing summary of an order (UC-28)", "Provisional or final" and "Missing prices": the 360.50 example line by line; 3 × 0.10 = 0.30 exactly; everything zero; the state given; one, several and every missing price (no unit price, no amount, no total, `MissingPrices` in line order); lines always in the order powder, caps, weapon rentals, flask rentals; negative quantities refused; the largest edition of D4 exact. Then implement `Summarise`. Verify: the tests pass.
- [x] 2.3 Write a unit test that summing the quantities of several orders and pricing them once gives the sum of the orders' totals (the edition billing, design D1; `Sum` was dropped after the group 2 review). Verify: the tests pass, with 100 % line coverage of the calculator.
- [x] 2.4 Document the `Billing` module in `backend/src/Modules/README.md` as the second module without data, and state that later modules price orders through `IBillingCalculator`, never by hand. Verify: the README matches design D1.
- [x] 2.5 Review group 2 in parallel with `csharp-reviewer` and `type-design-analyzer`. Fix CRITICAL/HIGH findings.

## 3. Prices in the edition snapshot

- [x] 3.1 Write tests for `IEditionDirectory`: `FindAsync`, `GetCurrentAsync` and `ReadForOrderWriteAsync` return the edition's four prices, and `null` for each price not set in a `DRAFT` edition. Then add `EditionPrices` and `EditionSnapshot.Prices` (design D2), fill them in `EditionDirectory` (including the `EditionRow` locking read), and update every test helper that builds a snapshot. Verify: the new tests and the existing editions and orders tests pass.
- [x] 3.2 Review group 3 with `csharp-reviewer`. Fix CRITICAL/HIGH findings.

## 4. Billing in the orders API

- [x] 4.1 Write endpoint tests (Testcontainers) for `GET /api/orders/{id}`:
  - the 360.50 example of the spec, built with synthetic data, returns its four lines and total with two decimals, `PROVISIONAL` while `DRAFT` and `SUBMITTED`, `FINAL` once `VALIDATED`, and `PROVISIONAL` again after a return;
  - an entry edit, an Admin edit of a `VALIDATED` order and an Admin price change are reflected on the next read;
  - an entry of an arquebusier deleted while the orders are closed still counts; an entry removed by a deletion while the orders are open no longer does;
  - an order of an edition moved back to `DRAFT` without `flaskRental` returns no total and `missingPrices: ["FLASK_RENTAL"]`;
  - a FiringChief gets `404` for another comparsa's order and no billing in it.

  Then add `BillingMapping` (design D2), `BillingSummaryResponse`, `BillingLineResponse` and `OrderResponse.Billing` (design D3) in `OrderViews`. Verify: the tests pass.
- [x] 4.2 Write endpoint tests for a write that returns the order (entry edit with a rented flask): the response's billing already counts the new flask. Verify: the test passes without further code, or fix `OrderViews` until it does.
- [x] 4.3 Write endpoint tests for `GET /api/orders/overview`:
  - each prepared row has its billing and a not-prepared row has `null`;
  - an Admin gets `editionBilling` summing every prepared order (360.50 + 120.00 = 480.50), `PROVISIONAL` while one is not `VALIDATED` and `FINAL` when all are, and zero and `PROVISIONAL` with no prepared order;
  - a FiringChief gets only their comparsas' rows and `editionBilling: null`.

  Then add `OverviewRowResponse.Billing` and `OverviewResponse.EditionBilling` in `OrderOverview`. Verify: the tests pass.
- [x] 4.4 Write a test that reading an order and the overview records no audit entry ("Billing is not audited"). Verify: the test passes.
- [x] 4.5 Regenerate `contracts/openapi.json` with the API build and check `OpenApiDocumentTests`: the new schemas, the `BillingConcept` and `BillingState` codes, and nullable amounts. Verify: the tests pass and the diff of the document is additive only.
- [x] 4.6 Update `docs/data-model.md`: describe `BillingSummary` (the four lines, the prices used, always derived, provisional or final, missing prices) in place of the "#11" note on `ComparsaOrder`. Add a UC-28 note to `docs/use-cases.md` with the maintainer decisions (live prices, provisional until validated, per concept only, no download). Verify: the docs match the spec, and no real data from `docs/sources/` appears.
- [x] 4.7 Review group 4 in parallel with `csharp-reviewer`, `security-reviewer` (scope and `404` of billing data) and `silent-failure-hunter` (missing prices must never be read as zero). Fix CRITICAL/HIGH findings.

## 5. Frontend foundations

- [x] 5.1 Regenerate the API client (`npm run generate:api`) and fix the test data of `features/comparsa-orders/test-data.ts` with a synthetic billing for every order and overview row. Verify: `npm run typecheck` and the existing orders tests pass.
- [x] 5.2 Write tests (Vitest + Testing Library + axe) for the new `components/app/AmountTable` composite (design D5): caption, a row per line, the total in `<tfoot>` as a row header, an empty amount cell for a missing price, and the stacked layout below `sm` keeping quantity and unit price readable. Then implement it, add its Storybook story (including the longest ca-ES-valencia labels and a missing price), and describe it in `docs/design/patterns.md`. Verify: the tests and the Storybook axe check pass.
- [x] 5.3 Write a test for the `billing` status kind (`PROVISIONAL`, `FINAL`) with its icons and labels. Then add it to `components/app/status.ts` and `ui:status.billing.*` in three locales. Verify: the test passes, and `npm run check-i18n` passes.
- [x] 5.4 Add the `billing` namespace in `es-ES`, `ca-ES-valencia` and `en` (design D5) and register it in `i18n/config.ts` and the typed resources, with `billingText.ts` (concept labels, plural-aware quantity texts, missing price names) and its unit tests in the three locales. Verify: the tests pass, and `npm run check-i18n` passes.
- [x] 5.5 Review group 5 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 6. Billing screens

- [x] 6.1 Write tests for `features/billing/components/BillingSummarySection`: the "Order page in Spanish" scenario (275,00 €, 13,50 €, 60,00 €, 12,00 €, total 360,50 €, provisional note), the final state, and the "Missing price on screen" scenario (the quantity without an amount, the alert naming the caps box price, no total); axe passes. Then implement it. Verify: the tests pass.
- [x] 6.2 Write tests for `BillingAmount`: a total as currency with its state badge in en ("€360.50", final), and "price missing" when the total is null. Then implement it. Verify: the tests pass.
- [x] 6.3 Write `OrderPage` tests: the billing section follows the totals and changes after an entry save returns the new order; a read-only past order still shows it. Then render it in `OrderPage`. Verify: the tests pass, and axe passes.
- [x] 6.4 Write `OrdersOverviewPage` tests: the "Amount" column for prepared rows (table and phone rows), nothing for a not-prepared row ("Not prepared row"), the edition billing after the edition totals for Admins only. Then implement them. Verify: the tests pass, and axe passes.
- [x] 6.5 Review group 6 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 7. End-to-end and verification

- [x] 7.1 Extend `e2e/orders.spec.ts`, using the seeded data read-only: the FiringChief's order shows the billing summary with the seeded edition's prices and the provisional note; the Admin's overview shows an amount per prepared row and the edition billing; on a 360 px viewport the billing summary does not scroll the page horizontally; axe runs on both pages. Verify: the spec passes in the compose stack.
- [x] 7.2 Extend `e2e/serial-state/order-review.spec.ts`: while the order is returned its billing is provisional, and after the Admin validates it the billing is final for the Admin and the FiringChief (the `afterEach` returns and resubmits it, so the next run starts provisional again). Verify: the spec passes, and the full suite passes twice in a row.
- [x] 7.3 Run `verification-loop`:
  - build, types and lint;
  - backend and frontend tests with coverage of at least 80 % on the `Billing` module and the changed orders and editions code;
  - a security grep: no new route, no amount in logs or audit entries, no personal data in the billing responses;
  - a diff review.

  Run `e2e-runner` and `pr-test-analyzer` on the change. Verify: the PASS report, with the findings and follow-ups recorded in design.md. `docs/mvp.md` marks #11 done when the change is archived.
