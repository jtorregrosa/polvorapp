import { Table2 } from 'lucide-react';
import { useId, useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useFormatters } from '@/lib/format';
import { Button } from './Button';
import type { ChartDataProps, ChartPoint } from './chart-data';
import { DataTable, type DataTableColumn } from './DataTable';
import { SectionCard } from './SectionCard';

export interface ChartFrameProps extends ChartDataProps {
  /**
   * The drawing (from `LineChart` or `BarChart`). Without one, the frame shows the table only, e.g.
   * when there are too few points to chart.
   */
  children?: ReactNode;
}

/**
 * A chart's heading, summary and data table (spec: Charts). The table holds every value of the
 * drawing, so no figure is available only in it (WCAG 1.1.1): it sits behind a "Show table" toggle
 * under a drawing, and is shown directly without one. Provisional values are marked in the table
 * with the word "provisional", not by style alone.
 */
export function ChartFrame({
  title,
  summary,
  note,
  categoryLabel,
  series,
  data,
  formatValue,
  children,
}: ChartFrameProps) {
  const { t } = useTranslation('ui');
  const format = useFormatters();
  const [showTable, setShowTable] = useState(false);
  const tableId = useId();
  const valueText = useMemo(
    () => formatValue ?? ((value: number) => format.number(value)),
    [formatValue, format],
  );

  const columns = useMemo<DataTableColumn<ChartPoint>[]>(
    () => [
      {
        id: 'category',
        header: categoryLabel,
        rowHeader: true,
        cell: (point) =>
          point.provisional ? t('chart.provisionalLabel', { label: point.label }) : point.label,
      },
      ...series.map((item): DataTableColumn<ChartPoint> => ({
        id: item.key,
        header: item.label,
        align: 'end',
        cell: (point) => valueText(point.values[item.key] ?? 0),
      })),
    ],
    [categoryLabel, series, t, valueText],
  );

  const table = (
    <DataTable
      caption={title}
      data={data}
      columns={columns}
      getRowId={(point) => point.id}
      paginated={false}
    />
  );

  return (
    <SectionCard title={title}>
      <div className="flex flex-col gap-1">
        <p className="text-body text-foreground">{summary}</p>
        {note && <p className="text-help text-muted-foreground">{note}</p>}
      </div>
      {children ? (
        <>
          {children}
          <div className="flex flex-col gap-3">
            <Button
              variant="quiet"
              size="sm"
              icon={Table2}
              aria-expanded={showTable}
              aria-controls={tableId}
              className="self-start"
              onClick={() => {
                setShowTable((shown) => !shown);
              }}
            >
              {showTable ? t('chart.hideTable') : t('chart.showTable')}
            </Button>
            <div id={tableId} hidden={!showTable}>
              {showTable && table}
            </div>
          </div>
        </>
      ) : (
        table
      )}
    </SectionCard>
  );
}
