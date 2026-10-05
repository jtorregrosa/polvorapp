# Tasks

## 1. Research and decision

- [x] 1.1 Check with Context7, and with `gh search code` for real-world use:
  - shadcn/ui `chart` (Recharts 3): `ChartContainer`, `ChartConfig`, `ChartTooltipContent`,
    `ChartLegendContent`;
  - Recharts 3 `accessibilityLayer` keyboard behaviour, SVG pattern fills, `isAnimationActive`,
    `ResponsiveContainer` sizing in jsdom tests;
  - EF Core 10 grouping translation for D1.

  Record versions and workarounds in design.md. Verify: design.md is updated, with no new open
  question.
- [x] 1.2 Move `docs/adr/0014-charts.md` to "Accepted" with the maintainer's approval, and update
  the ADR index. Verify: the ADR index lists it as accepted.

## 2. Backend

- [x] 2.1 Write Testcontainers integration tests for `IEditionTrends.ListAsync`:
  - draft editions left out;
  - the 10 most recent;
  - the in-progress edition provisional;
  - counts by status, powder, caps, source, rentals by kind and flasks;
  - first year, with the oldest edition unknown;
  - per-comparsa counts;
  - FiringChief scope.

  Then add the contract and implementation (D1). Verify: the tests and architecture tests pass.
- [x] 2.2 Write integration tests for `GET /compliance/trends`:
  - gender counts, with `UNKNOWN` for a deleted arquebusier;
  - Admin vs FiringChief;
  - `404` for an out-of-scope comparsa;
  - no names or identifiers in the JSON;
  - no audit entry.

  Then implement the endpoint (D2, D3) with OpenAPI docs. Verify: the tests pass and the OpenAPI
  document lists the endpoint.
- [x] 2.3 Review group 2 in parallel with `csharp-reviewer`, `database-reviewer` and
  `security-reviewer`. Fix CRITICAL/HIGH findings.

## 3. Chart composites

- [x] 3.1 Add `recharts` and the shadcn/ui `chart` primitive (D4), pinned. Record the licence in
  `docs/third-party-licenses.md`. Verify: build, lint and `npm ls recharts` pass.
- [x] 3.2 Write `contrast.test.ts` cases for `--chart-1..5` at 3:1 against `--card` in both themes,
  then add the tokens and document them in `docs/design/tokens.md`. Verify: the tests pass.
- [x] 3.3 Write tests and stories for `ChartFrame`, `LineChart` and `BarChart`:
  - heading and summary;
  - the table toggle with every value;
  - legend names;
  - patterns per series;
  - provisional marking;
  - formatted tooltips;
  - no animation under reduced motion;
  - axe in both themes and three languages.

  Then implement them. Update `motion.test.ts` if needed. Verify: the tests and the catalogue test
  pass.
- [x] 3.4 Document charts in `docs/design/patterns.md`: when to chart, table always, patterns,
  provisional. Verify: the design-docs test passes.
- [x] 3.5 Review group 3 in parallel with `react-reviewer`, `a11y-architect` and
  `typescript-reviewer`. Fix CRITICAL/HIGH findings.

## 4. Trends screen

- [x] 4.1 Regenerate the API client. Write `StatisticsPage` tests:
  - "Today" by default;
  - `?view=trends` selects "Trends";
  - the comparsa filter is shared and kept in the address.

  Then add the tabs (D5). Verify: the tests and axe pass.
- [x] 4.2 Write `TrendsTab` tests:
  - the six views with their summaries and tables, from MSW data;
  - provisional marking;
  - the fewer-than-two-editions message with the single table;
  - the FiringChief without per-comparsa table;
  - loading and error with retry;
  - 360 px layout.

  Then implement the tab, lazy-loaded. Verify: the tests and axe pass.
- [x] 4.3 Add the `insights:statistics.tabs.*`, `insights:trends.*` and `ui:chart.*` texts in
  es-ES, ca-ES-valencia and en. Verify: `npm run check-i18n` passes.
- [x] 4.4 Update `docs/use-cases.md` (UC-07 trends note). Verify: the note matches the screen.
- [ ] 4.5 Review group 4 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH
  findings.

## 5. End-to-end and verification

- [ ] 5.1 Extend `e2e/insights.spec.ts` on the seeded stack (which has past and current orders):
  - an Admin opens "Trends", sees the arquebusiers chart and opens its table;
  - a FiringChief sees only their comparsa;
  - the keyboard tooltip;
  - axe in both themes.

  Verify: the spec passes on the compose stack, and the suite passes twice in a row.
- [ ] 5.2 Run `verification-loop`:
  - build, types and lint;
  - backend and frontend tests with at least 80 % coverage on the new contract, endpoint,
    composites and tab;
  - the bundle check that the main chunk does not grow;
  - a security grep (counts only, scope);
  - a diff review.

  Run `e2e-runner` and `pr-test-analyzer`. Verify: the PASS report, with findings recorded in
  design.md.
