# Proposal

## Why

The Statistics page shows the registry as it is today. The maintainer wants to see how
participation evolves across festival editions: arquebusiers, women's share, newcomers, powder and
weapons. These are the figures the Federation needs to report and plan, and today it rebuilds them
from old spreadsheets. Every edition since PolvorApp went live keeps its orders and entries
(add-comparsa-orders), so the history is already in the database. It only needs to be aggregated
and drawn. PolvorApp has no chart library yet, so this change also picks one.

Capability (from `docs/mvp.md`): **`compliance-insights`**, extending **UC-07** (statistics and
equality report) with edition trends, and **`design-system`** for the chart composites. It relies
on:
- **BR-12**: the scope of FiringChiefs;
- **BR-05**: entry contents;
- `docs/compliance.md`: gender only for equality reports, counts only;
- **ADR-0001**: compliance-insights reads orders through `ComparsaOrders.Contracts`;
- **ADR-0009** and ADR-0011, and a new **ADR-0014** for the chart library;
- NFR-01 and NFR-07.

## What Changes

- **Edition trends API** (`GET /compliance/trends?comparsaId=`). One row per non-draft edition, the
  last 10, with:
  - active and reserve entries;
  - active entries by gender (from the registry today, `UNKNOWN` for deleted arquebusiers);
  - first-year arquebusiers;
  - powder kg and caps boxes;
  - weapon source of active entries, rentals by weapon kind, and flask rentals;
  - for multi-comparsa users, active entries per comparsa.

  The edition in progress is provisional. The response holds counts only and is scoped like the
  statistics.
- **Statistics page tabs**: "Today" (the current page) and "Trends", kept in the address.
- **Trends tab**: six views, each a chart with a heading, a summary sentence and its data table:
  - arquebusiers;
  - women's share;
  - first year;
  - powder and caps;
  - weapon sources and rentals by kind;
  - a per-comparsa table with the change against the previous edition.
- **Charts in the design system**: line and bar chart composites on shadcn/ui's `chart` (Recharts),
  with:
  - categorical chart tokens checked at 3:1;
  - patterns and legends;
  - keyboard tooltips;
  - provisional marking;
  - a table for every chart.
- **ADR-0014**: Recharts through shadcn/ui's chart component as the only chart library.

## Non-goals

- Registry history: monthly snapshots of license compliance, ages or course status over time.
  Gender per edition uses today's registry. Age brackets per edition are left out because they need
  birth dates at each festival date; that is a possible follow-up.
- Money trends (billing per edition). The billing module stays per edition for now.
- Importing figures of editions before PolvorApp.
- Downloads or exports of the trends (Exports owns downloads).
- Charts on other pages (dashboard, orders overview). The composites make that possible later.

## Capabilities

### New Capabilities
- none.

### Modified Capabilities
- `compliance-insights`:
  - "Statistics screen" gains the "Today" and "Trends" tabs;
  - new requirements "Edition trends (UC-07)" (API) and "Trends screen".
- `design-system`: new requirement "Charts".

## Impact

- **Backend**:
  - `ComparsaOrders.Contracts`: a new `IEditionTrends`, with per-edition entry aggregates for given
    comparsa ids and the first-year computation;
  - `ComparsaOrders`: its implementation, as one grouped query over `edition_entries` joined to
    orders and editions;
  - `ComplianceInsights`: the endpoint, scope and gender join through `IArquebusierRoster`;
  - integration tests.
- **Frontend**:
  - the `recharts` dependency and the shadcn/ui `chart` primitive;
  - `components/app/` `LineChart`, `BarChart`, `ChartFrame` (heading, summary, table toggle) with
    stories;
  - chart tokens;
  - `features/compliance-insights` (tabs, trends tab);
  - the generated client;
  - i18n in three locales.
- **Docs**:
  - `docs/adr/0014-charts.md` and the ADR index;
  - `docs/design/patterns.md` and `tokens.md`;
  - `docs/use-cases.md` (UC-07 notes);
  - `docs/third-party-licenses.md` (Recharts, MIT).
- **Security and GDPR**:
  - counts only, scoped on the server;
  - small groups can still be revealing (for example one woman in a small comparsa), which is the
    same exposure as the existing statistics and is acceptable under `docs/compliance.md`'s
    equality-report purpose.
