import { useMemo } from 'react';
import type { ChartConfig } from '@/components/ui/chart';
import { useFormatters } from '@/lib/format';

/** One series of a chart: its values come from `ChartPoint.values[key]`. */
export interface ChartSeries {
  key: string;
  /** Already translated; shown in the legend, the tooltip and the table header. */
  label: string;
  /** Formats this series' values instead of the chart's `formatValue` (e.g. a count beside shares). */
  formatValue?: (value: number) => string;
}

/** One category of a chart (e.g. an edition), with a value per series. */
export interface ChartPoint {
  id: string;
  /** Already formatted, e.g. the year. */
  label: string;
  /** Marks the point's values as provisional in the chart, the tooltip and the table. */
  provisional?: boolean;
  /** A value per series key; null (or missing) when there is no figure, shown as "—", never as 0. */
  values: Readonly<Record<string, number | null>>;
}

/**
 * The data every chart composite takes and turns into a drawing, a legend and a table. Keep `series`,
 * `tableSeries`, `data` and `formatValue` stable (module constants or memoised): the table's columns
 * are derived from them.
 */
export interface ChartDataProps {
  /** Already translated; the chart's heading. */
  title: string;
  /** Already translated; one sentence that says what the chart shows. */
  summary: string;
  /** Already translated; a quieter note under the summary, e.g. where a figure comes from. */
  note?: string;
  /** Header of the category column of the table, e.g. "Edition". */
  categoryLabel: string;
  series: readonly ChartSeries[];
  /** Series shown only in the table, after the drawn ones, e.g. a count that explains a share. */
  tableSeries?: readonly ChartSeries[];
  /** The categories in order; each label is unique. */
  data: readonly ChartPoint[];
  /** Formats a value for the table, the tooltip and the axis; numbers in the user's language otherwise. */
  formatValue?: (value: number) => string;
}

/** Formats a chart's values: `formatValue`, or numbers in the user's language. */
export function useValueText(formatValue?: (value: number) => string): (value: number) => string {
  const format = useFormatters();
  return useMemo(() => formatValue ?? ((value: number) => format.number(value)), [formatValue, format]);
}

/** Series colours, one categorical token each (spec: Charts; contrast in contrast.test.ts). */
const TONES = ['chart-1', 'chart-2', 'chart-3', 'chart-4', 'chart-5'] as const;

/** The colour token of the series at `index`, as a CSS value. */
export function seriesColor(index: number): string {
  return `var(--${TONES[index % TONES.length] ?? 'chart-1'})`;
}

/** The data key of the series at `index` inside the drawing, safe whatever the series key is. */
export function seriesKey(index: number): string {
  return `s${String(index)}`;
}

/** The pattern id of the series at `index` in the chart `chartId`. */
export function patternId(chartId: string, index: number): string {
  return `${chartId}-pattern-${String(index)}`;
}

/** A row Recharts draws: the category label plus one value per series, under `seriesKey`. */
export type DrawnRow = { label: string; provisional: boolean } & Record<
  string,
  number | string | boolean | null
>;

/** The rows Recharts draws; a missing value stays null, so it is not drawn as 0. */
export function drawingData(series: readonly ChartSeries[], data: readonly ChartPoint[]): DrawnRow[] {
  return data.map((point) => ({
    ...Object.fromEntries(series.map((item, index) => [seriesKey(index), point.values[item.key] ?? null])),
    label: point.label,
    provisional: point.provisional === true,
  }));
}

/** The shadcn/ui chart config: each drawn series with its name and colour. */
export function chartConfig(series: readonly ChartSeries[]): ChartConfig {
  return Object.fromEntries(
    series.map((item, index) => [seriesKey(index), { label: item.label, color: seriesColor(index) }]),
  );
}

/** The marker of a line series: circle, square, triangle, diamond or cross (spec: Charts). */
export function markerPath(index: number, x: number, y: number, size = 4): string {
  const s = size;
  switch (index % 5) {
    case 1:
      return `M${String(x - s)},${String(y - s)}h${String(2 * s)}v${String(2 * s)}h${String(-2 * s)}z`;
    case 2:
      return `M${String(x)},${String(y - s - 1)}L${String(x + s + 1)},${String(y + s)}H${String(x - s - 1)}z`;
    case 3:
      return `M${String(x)},${String(y - s - 1)}L${String(x + s + 1)},${String(y)}L${String(x)},${String(y + s + 1)}L${String(x - s - 1)},${String(y)}z`;
    case 4:
      return `M${String(x - s)},${String(y - s)}L${String(x + s)},${String(y + s)}M${String(x + s)},${String(y - s)}L${String(x - s)},${String(y + s)}`;
    default:
      return `M${String(x - s)},${String(y)}a${String(s)},${String(s)} 0 1,0 ${String(2 * s)},0a${String(s)},${String(s)} 0 1,0 ${String(-2 * s)},0`;
  }
}

/** Whether a drawn row (a Recharts payload) is a provisional category. */
export function isProvisionalRow(row: unknown): boolean {
  return typeof row === 'object' && row !== null && 'provisional' in row && row.provisional === true;
}

/** The tooltip heading of a category: its label, with "provisional" when it is. */
export function categoryHeading(
  label: unknown,
  payload: readonly { payload?: unknown }[],
  provisionalLabel: (label: string) => string,
): string {
  const text = typeof label === 'string' || typeof label === 'number' ? String(label) : '';
  return isProvisionalRow(payload[0]?.payload) ? provisionalLabel(text) : text;
}
