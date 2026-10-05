import { useId, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import * as Recharts from 'recharts';
import { ChartContainer, ChartTooltip, ChartTooltipContent } from '@/components/ui/chart';
import { useFormatters } from '@/lib/format';
import { CategoryTick, ChartLegendList, SeriesPatterns } from './chart-parts';
import {
  categoryHeading,
  chartConfig,
  type ChartDataProps,
  drawingData,
  isProvisionalRow,
  patternId,
  seriesKey,
} from './chart-data';
import { ChartFrame } from './ChartFrame';

export interface BarChartProps extends ChartDataProps {
  /**
   * `grouped` puts the series side by side, `stacked` on top of each other, and `percent` stacks
   * them to 100 % of each category.
   */
  layout?: 'grouped' | 'stacked' | 'percent';
}

/**
 * Bars per category with their heading, summary, legend and table (spec: Charts). Each series has a
 * colour token and a pattern; a provisional category has a dashed outline and "provisional" under
 * its label. The chart takes focus and the arrow keys move between categories, each showing its
 * values in a tooltip (Recharts' accessibility layer). It fades in once, on first render, unless the
 * user asks for reduced motion; data changes are not animated.
 */
export function BarChart({ layout = 'grouped', ...props }: BarChartProps) {
  const { t } = useTranslation('ui');
  const format = useFormatters();
  const chartId = `bars-${useId().replace(/:/g, '')}`;
  const { title, summary, series, data, formatValue } = props;
  const valueText = useMemo(
    () => formatValue ?? ((value: number) => format.number(value)),
    [formatValue, format],
  );
  const rows = useMemo(() => drawingData(series, data), [series, data]);
  const config = useMemo(() => chartConfig(series), [series]);
  const provisional = useMemo(
    () => new Set(data.filter((point) => point.provisional).map((point) => point.label)),
    [data],
  );
  const stackId = layout === 'grouped' ? undefined : 'total';
  const percent = (value: number) => format.number(value, { style: 'percent', maximumFractionDigits: 0 });

  return (
    <ChartFrame {...props}>
      <div className="flex flex-col gap-3 motion-safe:animate-in motion-safe:duration-200 motion-safe:fade-in-0">
        <SeriesPatterns chartId={chartId} count={series.length} />
        <ChartContainer config={config} className="aspect-auto h-64 w-full">
          <Recharts.BarChart
            accessibilityLayer
            title={title}
            desc={summary}
            data={rows}
            stackOffset={layout === 'percent' ? 'expand' : 'none'}
            margin={{ top: 8, right: 8, bottom: 0, left: 0 }}
          >
            <Recharts.CartesianGrid vertical={false} />
            <Recharts.XAxis
              dataKey="label"
              tickLine={false}
              axisLine={false}
              interval={0}
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
            <Recharts.YAxis
              tickLine={false}
              axisLine={false}
              width={48}
              allowDecimals={false}
              tickFormatter={(value: number) => (layout === 'percent' ? percent(value) : valueText(value))}
            />
            <ChartTooltip
              cursor={{ className: 'fill-muted' }}
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
            {series.map((item, index) => (
              <Recharts.Bar
                key={item.key}
                dataKey={seriesKey(index)}
                name={item.label}
                stackId={stackId}
                fill={`url(#${patternId(chartId, index)})`}
                isAnimationActive={false}
                shape={(bar: Recharts.BarShapeProps) =>
                  isProvisionalRow(bar.payload) ? (
                    <Recharts.Rectangle
                      {...bar}
                      stroke="var(--foreground)"
                      strokeWidth={1.5}
                      strokeDasharray="4 2"
                    />
                  ) : (
                    <Recharts.Rectangle {...bar} />
                  )
                }
              />
            ))}
          </Recharts.BarChart>
        </ChartContainer>
        <ChartLegendList chartId={chartId} series={series} kind="bar" />
      </div>
    </ChartFrame>
  );
}
