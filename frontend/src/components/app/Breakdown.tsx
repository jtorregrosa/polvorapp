import { useId } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useFormatters } from '@/lib/format';

export interface BreakdownColumn {
  id: string;
  /** Already translated. */
  label: string;
}

export interface BreakdownRow {
  id: string;
  /** Already translated. */
  label: string;
  /** One count per column, in column order (as many as there are columns). */
  counts: readonly number[];
}

export interface BreakdownProps {
  /** Already translated; names the table. */
  title: string;
  /** Header of the category column (e.g. "Age bracket"), already translated. */
  categoryLabel: string;
  /** The count columns: one for a plain breakdown, several to split it (e.g. by gender). */
  columns: readonly BreakdownColumn[];
  rows: readonly BreakdownRow[];
  /** The total each row's share is taken of, at least every row's sum; with 0, no share is shown. */
  total: number;
  /** Adds a column with each row's total, for breakdowns with several count columns. */
  showRowTotal?: boolean;
  /** Header of the share column, already translated, when the total is not "the total" (e.g. "% of the weapons"). */
  shareLabel?: string;
}

/**
 * Counts of a dashboard with each row's share of a total, as text and as a native meter (spec:
 * Breakdown figures). The text carries the share for everyone; the meter only repeats it visually,
 * so it is hidden from assistive technology and the share is never announced twice nor given by the
 * bar alone. The table scrolls inside its own focusable region on narrow screens, like `DataTable`.
 */
export function Breakdown({
  title,
  categoryLabel,
  columns,
  rows,
  total,
  showRowTotal = false,
  shareLabel,
}: BreakdownProps) {
  const { t } = useTranslation('ui');
  const format = useFormatters();
  const captionId = useId();
  const hasShare = total > 0;

  return (
    <Table
      container={{
        role: 'region',
        'aria-labelledby': captionId,
        tabIndex: 0,
        className: 'rounded-lg border bg-card',
      }}
    >
      <TableCaption id={captionId} className="sr-only">
        {title}
      </TableCaption>
      <TableHeader>
        <TableRow>
          <TableHead scope="col">{categoryLabel}</TableHead>
          {columns.map((column) => (
            <TableHead key={column.id} scope="col" className="text-end">
              {column.label}
            </TableHead>
          ))}
          {showRowTotal && (
            <TableHead scope="col" className="text-end">
              {t('breakdown.total')}
            </TableHead>
          )}
          {hasShare && <TableHead scope="col">{shareLabel ?? t('breakdown.share')}</TableHead>}
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row) => {
          const sum = row.counts.reduce((a, b) => a + b, 0);
          const share = hasShare ? sum / total : 0;
          const shareText = format.number(share, { style: 'percent', maximumFractionDigits: 0 });
          return (
            <TableRow key={row.id}>
              <TableHead scope="row" className="min-w-24 font-normal whitespace-normal">
                {row.label}
              </TableHead>
              {columns.map((column, index) => (
                <TableCell key={column.id} className="text-end tabular-nums">
                  {format.number(row.counts[index] ?? 0)}
                </TableCell>
              ))}
              {showRowTotal && (
                <TableCell className="text-end font-medium tabular-nums">{format.number(sum)}</TableCell>
              )}
              {hasShare && (
                <TableCell>
                  <span className="flex items-center gap-2 sm:min-w-28">
                    <span className="w-12 shrink-0 text-end tabular-nums">{shareText}</span>
                    <meter
                      data-slot="meter"
                      aria-hidden="true"
                      min={0}
                      max={100}
                      value={Math.min(100, Math.round(share * 100))}
                      className="hidden h-1.5 w-full sm:block"
                    />
                  </span>
                </TableCell>
              )}
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}
