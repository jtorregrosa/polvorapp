# Design

## Context

See `proposal.md` and the delta specs. The facts below come from the current code.

- **Statistics today.** `ComplianceInsights` serves `GET /compliance/statistics`:
  - `ScopedFacts` reads the registry facts within `ComparsaAccess` (BR-12);
  - `ComplianceInsightsQueries` aggregates them;
  - the first year comes from `ComparsaOrders.Contracts.IParticipationHistory.FirstYearAsync`.
- **Entries.** `EditionEntry` (`ComparsaOrders`) holds, per edition:
  - `Status`, `PowderKg`, `CapsBoxes`, `WeaponSource`, `RentalWeaponModelId` and `Flask`;
  - a nullable `ArquebusierId`, null after a deletion.

  `Order` links entries to a comparsa and an edition. There is no gender in entries.
- **Overview.** `GET /comparsa-orders/overview?editionId=` already aggregates one edition, but per
  call and for the orders screens. It is not reusable for ten editions in one request.
- **Frontend.**
  - `StatisticsPage` uses `PageHeader`, `FilterSelect`, `StatCard`, `Breakdown` and `DataTable`;
  - `components/app/Tabs` exists (Radix);
  - routes are not lazy-loaded today;
  - no chart library is installed.

## Goals / Non-Goals

**Goals:**
- One request and one grouped SQL query per trends view, not one per edition.
- Module boundaries kept: compliance-insights reads only `ComparsaOrders.Contracts` and
  `ArquebusierRegistry.Contracts`.
- Charts are accessible by construction: a table always, keyboard tooltips, patterns.

**Non-Goals:** caching or pre-computing trends (at most 10 editions × ~800 entries is cheap).

## Decisions

### D1. `IEditionTrends` in `ComparsaOrders.Contracts`

```text
Task<IReadOnlyList<EditionTrendRow>> ListAsync(ComparsaAccess access, Guid? comparsaId, CancellationToken)
```

`EditionTrendRow` holds `Year`, `Provisional`, counts by status, `PowderKg`, `CapsBoxes`, counts by
weapon source, rentals by `WeaponKind`, flask rentals, first-year count (or null), and a map of
active entries per comparsa id. It also returns the ACTIVE entries' `ArquebusierId`s per edition,
used only inside the server for the gender join (D2), never serialised.
- The implementation is one query grouped by edition and comparsa over entries ⋈ orders ⋈
  editions (`status <> DRAFT`, latest 10 years). Rentals by kind come from the catalogue through
  `ICatalogDirectory.FindWeaponModelsAsync` for the distinct model ids.
- The first year reuses `IParticipationHistory`'s rule: an ACTIVE entry with no ACTIVE entry in an
  earlier edition. It is computed in SQL with a window over `arquebusier_id`, and is null for the
  oldest edition with orders.

*Alternative*: compute in `ComplianceInsights` from raw entries through a new "list entries"
contract. Rejected, because it moves ~8,000 rows across the boundary for counts.

### D2. Gender from the registry, counted on the server

`ComplianceInsights` asks `IArquebusierFacts` (already used by `ScopedFacts`) for the gender of the
returned arquebusier ids, within the same scope. Each edition's ACTIVE ids are counted by gender.
Ids that are null or not found count as `UNKNOWN`. Only counts leave the module (see
`docs/compliance.md`, gender for equality reports).

### D3. Endpoint

`GET /compliance/trends?comparsaId=` is in `ComplianceEndpoints`, with the same authorisation,
scope and `404` rules as `/statistics`. Its response is `TrendsResponse { rows: TrendRowResponse[],
comparsas?: { id, name }[] }`, and per-comparsa figures are present only for multi-comparsa scopes
without a filter. It is not audited, like `/statistics` (reads of aggregates).

### D4. Charts stack

- `npx shadcn add chart` adds `components/ui/chart.tsx` (Recharts 3). It is pinned in `package.json`
  and recorded in `docs/third-party-licenses.md`.
- Composites in `components/app/`:
  - `ChartFrame`: heading, summary, a "Show table" disclosure (`details`/`summary` styled as a quiet
    button) with a `DataTable`;
  - `LineChart`;
  - `BarChart` (`stacked`, `percent`).

  Each takes `series: { key, labelKey, tone }[]` and `data`.
- `accessibilityLayer` is on. Tooltips use the shadcn `ChartTooltipContent` with `Intl` formatting
  from `useFormatters`.
- Patterns: SVG `<pattern>` defs (diagonal, dots, cross-hatch, solid), assigned per series index;
  provisional values get an outline-only bar or a dashed line segment.
- Motion: `isAnimationActive` only on first mount, 200 ms, and off under
  `prefers-reduced-motion`.
- Tokens: `--chart-1..5` for light and dark, checked by `contrast.test.ts` at 3:1 against `--card`.
- The trends tab is `React.lazy`-loaded so Recharts stays out of the main bundle.

### D5. Screen

`StatisticsPage` wraps the current content in the "Today" tab and adds "Trends", selected by
`?view=trends`. The comparsa filter moves above the tabs and is shared. `TrendsTab` renders, in
order:
1. arquebusiers (stacked bars ACTIVE/RESERVE);
2. women's share (line, %, with the `UNKNOWN` count in the summary and table);
3. first year (bars);
4. powder kg and caps boxes (two small bar charts side by side, stacked on phones);
5. weapon source (100 % stacked bars) and rentals by kind (grouped bars);
6. per comparsa (a `DataTable` with years as columns and a Δ column).

Summary sentences come from i18n with plural and number formatting.

### D6. i18n keys (es-ES, ca-ES-valencia, en)

- `insights`:
  - `statistics.tabs.today`, `statistics.tabs.trends`;
  - `trends.*`: titles, summaries, axis labels, "provisional", "fewer than two editions", table
    captions and column headers;
  - series names reuse the existing `ui:status`/`tag` labels where they exist.
- `ui`: `chart.showTable`, `chart.hideTable`, `chart.provisional`.

## Risks / Trade-offs

- [Recharts' keyboard layer is less complete than a table] → The table is always offered, and the
  spec makes it the accessible source. The tooltips are an extra.
- [Gender from today's registry misstates past editions if a record changed] → Rare. The gender is
  documented in the UI note under the chart, and `UNKNOWN` is stated.
- [Bundle size] → The trends tab is lazy-loaded, and the size is checked in the verification step
  (the main chunk must not grow).
- [Small comparsas make counts identifying] → Same exposure as today's statistics, scoped by BR-12
  and limited to counts (`docs/compliance.md`).

## Migration Plan

No schema change. Deploy API and web together. Rollback is reverting the change.
