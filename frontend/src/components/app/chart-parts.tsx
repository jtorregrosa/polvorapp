import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { useActiveTooltipLabel } from 'recharts';
import { useFormatters } from '@/lib/format';
import { markerPath, patternId, seriesColor, type ChartPoint, type ChartSeries } from './chart-data';

/**
 * The patterns that tell bar series apart beyond colour (spec: Charts): solid, diagonal lines, dots,
 * cross-hatch and horizontal lines, drawn in the card colour over the series colour.
 */
export function SeriesPatterns({ chartId, count }: { chartId: string; count: number }) {
  return (
    <svg aria-hidden="true" focusable="false" width="0" height="0" className="absolute">
      <defs>
        {Array.from({ length: count }, (_, index) => (
          <pattern
            key={index}
            id={patternId(chartId, index)}
            width="6"
            height="6"
            patternUnits="userSpaceOnUse"
          >
            <rect width="6" height="6" fill={seriesColor(index)} />
            <PatternMarks variant={index % 5} />
          </pattern>
        ))}
      </defs>
    </svg>
  );
}

function PatternMarks({ variant }: { variant: number }) {
  switch (variant) {
    case 1:
      return <path d="M-1,1 l2,-2 M0,6 l6,-6 M5,7 l2,-2" className="stroke-card" strokeWidth="1.5" />;
    case 2:
      return <circle cx="3" cy="3" r="1.25" className="fill-card" />;
    case 3:
      return <path d="M0,0 L6,6 M6,0 L0,6" className="stroke-card" strokeWidth="1" />;
    case 4:
      return <path d="M0,3 H6" className="stroke-card" strokeWidth="1.5" />;
    default:
      return null;
  }
}

/** The legend under a chart: each series name with its swatch (pattern for bars, marker for lines). */
export function ChartLegendList({
  chartId,
  title,
  series,
  kind,
  hasProvisional,
}: {
  chartId: string;
  /** The chart's heading, so each legend has its own name. */
  title: string;
  series: readonly ChartSeries[];
  kind: 'bar' | 'line';
  /** Adds the key of the provisional marking. */
  hasProvisional: boolean;
}) {
  const { t } = useTranslation('ui');
  return (
    // An unbulleted list keeps its list role in Safari only when it is explicit.
    // eslint-disable-next-line jsx-a11y/no-redundant-roles
    <ul
      role="list"
      aria-label={t('chart.legendFor', { title })}
      className="flex flex-wrap gap-x-4 gap-y-1 text-help text-muted-foreground"
    >
      {series.map((item, index) => (
        <li key={item.key} className="flex items-center gap-1.5">
          <svg aria-hidden="true" focusable="false" viewBox="0 0 12 12" className="size-3 shrink-0">
            {kind === 'bar' ? (
              <rect width="12" height="12" rx="2" fill={`url(#${patternId(chartId, index)})`} />
            ) : (
              <path
                d={markerPath(index, 6, 6, 3.5)}
                fill={index % 5 === 4 ? 'none' : seriesColor(index)}
                stroke={seriesColor(index)}
                strokeWidth="1.5"
              />
            )}
          </svg>
          {item.label}
        </li>
      ))}
      {hasProvisional && (
        <li className="flex items-center gap-1.5">
          <svg aria-hidden="true" focusable="false" viewBox="0 0 12 12" className="size-3 shrink-0">
            {kind === 'bar' ? (
              <rect
                x="1"
                y="1"
                width="10"
                height="10"
                fill="none"
                className="stroke-foreground"
                strokeWidth="1.5"
                strokeDasharray="3 2"
              />
            ) : (
              <path d="M0,6 H12" className="stroke-foreground" strokeWidth="1.5" strokeDasharray="3 2" />
            )}
          </svg>
          {kind === 'bar' ? t('chart.provisionalBars') : t('chart.provisionalLines')}
        </li>
      )}
    </ul>
  );
}

/** How to use the focusable drawing from the keyboard, for its `aria-describedby`. */
export function ChartKeyboardHint({ id }: { id: string }) {
  const { t } = useTranslation('ui');
  return (
    <p id={id} className="sr-only">
      {t('chart.keyboardHint')}
    </p>
  );
}

/**
 * Says the active category's values in `target`, a polite live region outside the drawing: the
 * tooltip is drawn, not announced, so moving with the arrow keys is heard through this (spec:
 * Charts, keyboard tooltip). Rendered inside the Recharts chart to read its active category.
 */
export function ChartAnnouncer({
  target,
  data,
  series,
  valueText,
}: {
  target: HTMLElement | null;
  data: readonly ChartPoint[];
  series: readonly ChartSeries[];
  valueText: (value: number) => string;
}) {
  const { t } = useTranslation('ui');
  const format = useFormatters();
  const label = useActiveTooltipLabel();
  if (!target) return null;
  const point = data.find((candidate) => candidate.label === label);
  const text = point
    ? t('chart.announcement', {
        category: point.provisional ? t('chart.provisionalLabel', { label: point.label }) : point.label,
        values: format.list(
          series.map((item) => {
            const value = point.values[item.key];
            return `${item.label} ${value === null || value === undefined ? t('chart.noValue') : (item.formatValue ?? valueText)(value)}`;
          }),
        ),
      })
    : '';
  return createPortal(text, target);
}
