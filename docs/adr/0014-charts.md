# 0014. Charts with Recharts through shadcn/ui

- Status: Accepted (the maintainer asked to apply `add-statistics-trends`, 2026-10-05)
- Date: 2026-10-05

## Context

The statistics trends (`add-statistics-trends`) need line and bar charts. PolvorApp has no chart
library. Its bars are drawn by the in-house `Breakdown` composite, which shows shares, not series
over time. ADR-0009 fixed shadcn/ui and Tailwind as the design-system base. A chart library must
meet NFR-07 (WCAG 2.2 AA): keyboard access to values, no information by colour alone, 3:1 for
graphical objects, and a text alternative. It must also theme from CSS variables in light and dark.

## Decision

- Use **Recharts 3** (MIT) through the **shadcn/ui `chart` component**. The component wraps Recharts
  with a `ChartConfig` that maps series to CSS variables, and a themed tooltip and legend.
- Feature screens use only the `components/app` chart composites, never Recharts directly
  (ADR-0009). The composites add:
  - the heading;
  - the summary sentence;
  - the data table;
  - patterns for series;
  - provisional marking;
  - the motion rules.
- Recharts' `accessibilityLayer` provides keyboard navigation of data points. The composites
  provide the table alternative.
- Series colours come from new categorical chart tokens, checked at 3:1 against the card surface
  in both themes.

## Consequences

- One new runtime dependency (Recharts and its d3 sub-packages). It is loaded only when the
  "Trends" tab opens (`React.lazy`), so other pages do not pay for it.
- The chart look follows the design tokens and stays consistent with shadcn/ui updates.
- Charts render as SVG, so they print and scale. The tables remain the accessible source of truth.

## Alternatives considered

- **visx** (Airbnb): flexible low-level primitives, but more code to own for axes, tooltips and
  keyboard access, and no shadcn/ui integration.
- **Chart.js** (via react-chartjs-2): canvas rendering, which is harder to make accessible and to
  theme from CSS variables.
- **Nivo**: rich and themable, but heavier, with its own theming system beside the tokens.
- **Extending `Breakdown`**: enough for shares, not for series over time.
