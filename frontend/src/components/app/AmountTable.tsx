import { useTranslation } from 'react-i18next';
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

export interface AmountTableRow {
  id: string;
  /** Already translated. */
  label: string;
  /** Already formatted, with its unit (e.g. "5 kg"). */
  quantity: string;
  /** Already formatted as currency; null when there is no price. */
  unitPrice: string | null;
  /** Already formatted as currency; null when there is no price. */
  amount: string | null;
}

export interface AmountTableProps {
  /** Already translated; names the table. */
  title: string;
  rows: readonly AmountTableRow[];
  /** Already formatted as currency; null shows no total row (e.g. a price is missing). */
  total: string | null;
}

/** A missing price, in words for everyone: a dash alone would not say why there is no amount. */
function NoPrice() {
  const { t } = useTranslation('ui');
  return <span className="text-muted-foreground">{t('amountTable.noPrice')}</span>;
}

/**
 * Money lines as "quantity × unit price = amount" with a total row (spec: Billing screens). The
 * values come formatted by the caller; without a total (a price is missing) the caller says why
 * next to the table. Below `sm` the quantity and unit price columns are hidden and
 * repeated under the concept, so the table keeps two columns and never scrolls on a phone; the total
 * is the table footer, a row header with its amount.
 */
export function AmountTable({ title, rows, total }: AmountTableProps) {
  const { t } = useTranslation('ui');
  const wide = 'hidden sm:table-cell';

  return (
    <Table container={{ className: 'rounded-lg border bg-card' }}>
      <TableCaption className="sr-only">{title}</TableCaption>
      <TableHeader>
        <TableRow>
          <TableHead scope="col">{t('amountTable.concept')}</TableHead>
          <TableHead scope="col" className={`${wide} text-end`}>
            {t('amountTable.quantity')}
          </TableHead>
          <TableHead scope="col" className={`${wide} text-end`}>
            {t('amountTable.unitPrice')}
          </TableHead>
          <TableHead scope="col" className="text-end">
            {t('amountTable.amount')}
          </TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row) => (
          <TableRow key={row.id}>
            <TableHead scope="row" className="font-normal break-words whitespace-normal">
              {row.label}{' '}
              <span className="block text-muted-foreground tabular-nums sm:hidden">
                {row.unitPrice === null ? row.quantity : `${row.quantity} × ${row.unitPrice}`}
              </span>
            </TableHead>
            <TableCell className={`${wide} text-end tabular-nums`}>{row.quantity}</TableCell>
            <TableCell className={`${wide} text-end tabular-nums`}>{row.unitPrice ?? <NoPrice />}</TableCell>
            <TableCell className="text-end tabular-nums">{row.amount ?? <NoPrice />}</TableCell>
          </TableRow>
        ))}
      </TableBody>
      {total !== null && (
        <TableFooter>
          <TableRow>
            <TableHead scope="row">{t('amountTable.total')}</TableHead>
            <TableCell className={wide} />
            <TableCell className={wide} />
            <TableCell className="text-end font-semibold tabular-nums">{total}</TableCell>
          </TableRow>
        </TableFooter>
      )}
    </Table>
  );
}
