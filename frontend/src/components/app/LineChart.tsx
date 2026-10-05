import { useId, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import * as Recharts from 'recharts';
import { ChartContainer, ChartTooltip, ChartTooltipContent } from '@/components/ui/chart';
import { ChartAnnouncer, ChartKeyboardHint, ChartLegendList } from './chart-parts';
import {
  categoryHeading,
  chartConfig,
  type ChartDataProps,
  drawingData,
  type DrawnRow,
  markerPath,
  seriesColor,
  seriesKey,
  useValueText,
} from './chart-data';
import { ChartFrame } from './ChartFrame';

export type LineChartProps = ChartDataProps;

interface DotProps {
  cx?: number;
  cy?: number;
  index?: number;
}

/** The marker of series `seriesIndex` at a point; hollow when the point is provisional. */
function renderMarker(seriesIndex: number, hollow: boolean, size: number, { cx, cy, index }: DotProps) {
  const key = `marker-${String(index)}`;
  if (cx === undefined || cy === undefined) return <g key={key} />;
  return (
    <path
      key={key}
      d={markerPath(seriesIndex, cx, cy, size)}
      fill={hollow || seriesIndex % 5 === 4 ? 'var(--card)' : seriesColor(seriesIndex)}
      stroke={seriesColor(seriesIndex)}
      strokeWidth={2}
    />
  );
}

/**
 * Each series as a solid line between settled points and a dashed one along every segment that
 * touches a provisional point; the series' own key keeps every value for the tooltip.
 */
function lineRows(rows: readonly DrawnRow[], seriesCount: number): DrawnRow[] {
  return rows.map((row, position) => {
    const touchesProvisional =
      row.provisional || rows[position - 1]?.provisional === true || rows[position + 1]?.provisional === true;
    const drawn: DrawnRow = { ...row };
    for (let index = 0; index < seriesCount; index += 1) {
      const key = seriesKey(index);
      const value = row[key] ?? null;
      drawn[`${key}solid`] = row.provisional ? null : value;
      drawn[`${key}dashed`] = touchesProvisional ? value : null;
    }
    return drawn;
  });
}

/**
 * Lines per category with their heading, summary, legend and table (spec: Charts). Each series has a
 * colour token and its own marker shape; the segments into a provisional category are dashed and its
 * marker hollow, explained in the legend. The chart takes focus and the arrow keys move between
 * categories, each showing its values in a tooltip (Recharts' accessibility layer) and saying them in
 * a live region. It fades in once, on first render, unless the user asks for reduced motion.
 */
export function LineChart(props: LineChartProps) {
  const { t } = useTranslation('ui');
  const id = useId();
  const chartId = `lines${id.replace(/[^A-Za-z0-9_-]/g, '')}`;
  const [liveRegion, setLiveRegion] = useState<HTMLElement | null>(null);
  const { title, series, data, formatValue } = props;
  const valueText = useValueText(formatValue);
  const config = useMemo(() => chartConfig(series), [series]);
  const rows = useMemo(() => lineRows(drawingData(series, data), series.length), [series, data]);
  const hasProvisional = data.some((point) => point.provisional);
  const isProvisional = (index: number | undefined) => rows[index ?? -1]?.provisional === true;

  return (
    <ChartFrame {...props}>
      <div className="flex flex-col gap-3 motion-safe:animate-in motion-safe:duration-200 motion-safe:fade-in-0">
        <ChartKeyboardHint id={`${chartId}-hint`} />
        <ChartContainer config={config} className="aspect-auto h-64 w-full">
          <Recharts.LineChart
            accessibilityLayer
            aria-label={title}
            aria-roledescription={t('chart.roleDescription')}
            aria-describedby={`${chartId}-hint`}
            data={rows}
            margin={{ top: 8, right: 16, bottom: 0, left: 0 }}
          >
            <Recharts.CartesianGrid vertical={false} />
            <Recharts.XAxis
              dataKey="label"
              tickLine={false}
              axisLine={false}
              interval="preserveStartEnd"
              padding={{ left: 16, right: 16 }}
            />
            <Recharts.YAxis
              tickLine={false}
              axisLine={false}
              width={48}
              interval={0}
              tickFormatter={valueText}
            />
            <ChartTooltip
              cursor={{ className: 'stroke-border' }}
              content={
                <ChartTooltipContent
                  hideIndicator
                  valueFormatter={valueText}
                  labelFormatter={(label, payload) =>
                    categoryHeading(label, payload, (text) => t('chart.provisionalLabel', { label: text }))
                  }
                />
              }
            />
            {series.map((item, index) => [
              <Recharts.Line
                key={`${item.key}-solid`}
                dataKey={`${seriesKey(index)}solid`}
                stroke={seriesColor(index)}
                strokeWidth={2}
                dot={(dot: DotProps) => renderMarker(index, false, 4, dot)}
                activeDot={false}
                legendType="none"
                tooltipType="none"
                isAnimationActive={false}
              />,
              <Recharts.Line
                key={`${item.key}-dashed`}
                dataKey={`${seriesKey(index)}dashed`}
                stroke={seriesColor(index)}
                strokeWidth={2}
                strokeDasharray="5 4"
                dot={false}
                activeDot={false}
                legendType="none"
                tooltipType="none"
                isAnimationActive={false}
              />,
              <Recharts.Line
                key={item.key}
                dataKey={seriesKey(index)}
                name={item.label}
                stroke="none"
                dot={(dot: DotProps) =>
                  isProvisional(dot.index) ? (
                    renderMarker(index, true, 4, dot)
                  ) : (
                    <g key={`point-${String(dot.index)}`} />
                  )
                }
                activeDot={(dot: DotProps) => renderMarker(index, isProvisional(dot.index), 6, dot)}
                isAnimationActive={false}
              />,
            ])}
            <ChartAnnouncer target={liveRegion} data={data} series={series} valueText={valueText} />
          </Recharts.LineChart>
        </ChartContainer>
        <div ref={setLiveRegion} role="status" aria-atomic="true" className="sr-only" />
        <ChartLegendList
          chartId={chartId}
          title={title}
          series={series}
          kind="line"
          hasProvisional={hasProvisional}
        />
      </div>
    </ChartFrame>
  );
}
