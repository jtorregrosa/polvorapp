import { useId, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import * as Recharts from 'recharts';
import { ChartContainer, ChartTooltip, ChartTooltipContent } from '@/components/ui/chart';
import { useFormatters } from '@/lib/format';
import { ChartAnnouncer, ChartKeyboardHint, ChartLegendList, SeriesPatterns } from './chart-parts';
import {
  categoryHeading,
  chartConfig,
  type ChartDataProps,
  drawingData,
  isProvisionalRow,
  patternId,
  seriesKey,
  useValueText,
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
 * colour token and a pattern; a provisional category has a dashed outline, explained in the legend.
 * The chart takes focus and the arrow keys move between categories, each showing its values in a
 * tooltip (Recharts' accessibility layer) and saying them in a live region. It fades in once, on
 * first render, unless the user asks for reduced motion; data changes are not animated.
 */
export function BarChart({ layout = 'grouped', ...props }: BarChartProps) {
  const { t } = useTranslation('ui');
  const format = useFormatters();
  const id = useId();
  const chartId = `bars${id.replace(/[^A-Za-z0-9_-]/g, '')}`;
  const [liveRegion, setLiveRegion] = useState<HTMLElement | null>(null);
  const { title, series, data, formatValue } = props;
  const valueText = useValueText(formatValue);
  const rows = useMemo(() => drawingData(series, data), [series, data]);
  const config = useMemo(() => chartConfig(series), [series]);
  const hasProvisional = data.some((point) => point.provisional);
  const stacked = layout !== 'grouped';
  const percent = (value: number) => format.number(value, { style: 'percent', maximumFractionDigits: 0 });

  return (
    <ChartFrame {...props}>
      <div className="flex flex-col gap-3 motion-safe:animate-in motion-safe:duration-200 motion-safe:fade-in-0">
        <SeriesPatterns chartId={chartId} count={series.length} />
        <ChartKeyboardHint id={`${chartId}-hint`} />
        <ChartContainer config={config} className="aspect-auto h-64 w-full">
          <Recharts.BarChart
            accessibilityLayer
            aria-label={title}
            aria-roledescription={t('chart.roleDescription')}
            aria-describedby={`${chartId}-hint`}
            data={rows}
            stackOffset={layout === 'percent' ? 'expand' : 'none'}
            margin={{ top: 8, right: 8, bottom: 0, left: 0 }}
          >
            <Recharts.CartesianGrid vertical={false} />
            <Recharts.XAxis dataKey="label" tickLine={false} axisLine={false} interval="preserveStartEnd" />
            <Recharts.YAxis
              tickLine={false}
              axisLine={false}
              width={48}
              interval={0}
              allowDecimals={layout === 'percent'}
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
                stackId={stacked ? 'total' : undefined}
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
                    // A hairline in the card colour separates stacked segments (WCAG 1.4.11).
                    <Recharts.Rectangle {...bar} stroke={stacked ? 'var(--card)' : 'none'} strokeWidth={1} />
                  )
                }
              />
            ))}
            <ChartAnnouncer target={liveRegion} data={data} series={series} valueText={valueText} />
          </Recharts.BarChart>
        </ChartContainer>
        <div ref={setLiveRegion} role="status" aria-atomic="true" className="sr-only" />
        <ChartLegendList
          chartId={chartId}
          title={title}
          series={series}
          kind="bar"
          hasProvisional={hasProvisional}
        />
      </div>
    </ChartFrame>
  );
}
