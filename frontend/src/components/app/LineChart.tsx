import { useId, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import * as Recharts from 'recharts';
import { ChartContainer, ChartTooltip, ChartTooltipContent } from '@/components/ui/chart';
import { useFormatters } from '@/lib/format';
import { CategoryTick, ChartLegendList } from './chart-parts';
import {
  categoryHeading,
  chartConfig,
  type ChartDataProps,
  drawingData,
  markerPath,
  seriesColor,
  seriesKey,
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
  const cross = seriesIndex % 5 === 4;
  return (
    <path
      key={key}
      d={markerPath(seriesIndex, cx, cy, size)}
      fill={hollow || cross ? 'var(--card)' : seriesColor(seriesIndex)}
      stroke={seriesColor(seriesIndex)}
      strokeWidth={2}
    />
  );
}

/**
 * Lines per category with their heading, summary, legend and table (spec: Charts). Each series has a
 * colour token and its own marker shape; the segment into a provisional category is dashed, its
 * marker hollow, and "provisional" is written under its label. The chart takes focus and the arrow
 * keys move between categories, each showing its values in a tooltip (Recharts' accessibility
 * layer). It fades in once, on first render, unless the user asks for reduced motion.
 */
export function LineChart(props: LineChartProps) {
  const { t } = useTranslation('ui');
  const format = useFormatters();
  const chartId = `lines-${useId().replace(/:/g, '')}`;
  const { title, summary, series, data, formatValue } = props;
  const valueText = useMemo(
    () => formatValue ?? ((value: number) => format.number(value)),
    [formatValue, format],
  );
  const config = useMemo(() => chartConfig(series), [series]);
  const provisional = useMemo(
    () => new Set(data.filter((point) => point.provisional).map((point) => point.label)),
    [data],
  );
  // Each series is drawn as a solid line over settled points and a dashed one into provisional
  // points; a third, invisible line carries every value for the tooltip and the active marker.
  const rows = useMemo(
    () =>
      drawingData(series, data).map((row, position) => {
        const point = data[position];
        const next = data[position + 1];
        const drawn: Record<string, number | string | boolean | null> = { ...row };
        series.forEach((item, index) => {
          const key = seriesKey(index);
          const value = point?.values[item.key] ?? 0;
          drawn[`${key}solid`] = row.provisional ? null : value;
          drawn[`${key}dashed`] = row.provisional || next?.provisional ? value : null;
        });
        return drawn;
      }),
    [series, data],
  );

  return (
    <ChartFrame {...props}>
      <div className="flex flex-col gap-3 motion-safe:animate-in motion-safe:duration-200 motion-safe:fade-in-0">
        <ChartContainer config={config} className="aspect-auto h-64 w-full">
          <Recharts.LineChart
            accessibilityLayer
            title={title}
            desc={summary}
            data={rows}
            margin={{ top: 8, right: 16, bottom: 0, left: 0 }}
          >
            <Recharts.CartesianGrid vertical={false} />
            <Recharts.XAxis
              dataKey="label"
              tickLine={false}
              axisLine={false}
              interval={0}
              padding={{ left: 16, right: 16 }}
              height={provisional.size > 0 ? 40 : 24}
              tick={(tick: Recharts.XAxisTickContentProps) => (
                <CategoryTick
                  x={tick.x}
                  y={tick.y}
                  payload={tick.payload}
                  provisional={provisional}
                  provisionalText={t('chart.provisional')}
                />
              )}
            />
            <Recharts.YAxis tickLine={false} axisLine={false} width={48} tickFormatter={valueText} />
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
                  rows[dot.index ?? -1]?.provisional === true ? (
                    renderMarker(index, true, 4, dot)
                  ) : (
                    <g key={`point-${String(dot.index)}`} />
                  )
                }
                activeDot={(dot: DotProps) => renderMarker(index, false, 6, dot)}
                isAnimationActive={false}
              />,
            ])}
          </Recharts.LineChart>
        </ChartContainer>
        <ChartLegendList chartId={chartId} series={series} kind="line" />
      </div>
    </ChartFrame>
  );
}
