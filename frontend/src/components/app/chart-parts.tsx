import { useTranslation } from 'react-i18next';
import { markerPath, patternId, seriesColor, type ChartSeries } from './chart-data';

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
  series,
  kind,
}: {
  chartId: string;
  series: readonly ChartSeries[];
  kind: 'bar' | 'line';
}) {
  const { t } = useTranslation('ui');
  return (
    <ul
      aria-label={t('chart.legend')}
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
    </ul>
  );
}

/** An axis tick of the category axis: the label, and "provisional" under a provisional one. */
export function CategoryTick({
  x,
  y,
  payload,
  provisional,
  provisionalText,
}: {
  x?: number | string;
  y?: number | string;
  payload?: { value?: unknown };
  provisional: ReadonlySet<string>;
  provisionalText: string;
}) {
  const label = typeof payload?.value === 'string' ? payload.value : '';
  return (
    <g transform={`translate(${String(x ?? 0)},${String(y ?? 0)})`}>
      <text dy="0.71em" y={4} textAnchor="middle" className="fill-muted-foreground">
        {label}
      </text>
      {provisional.has(label) && (
        <text dy="0.71em" y={18} textAnchor="middle" className="fill-muted-foreground italic">
          {provisionalText}
        </text>
      )}
    </g>
  );
}
