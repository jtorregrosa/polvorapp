import { useTranslation } from 'react-i18next';
import type { BillingSummaryResponse } from '@/api/generated/model';
import { StatusBadge } from '@/components/app/StatusBadge';
import { cn } from '@/lib/cn';
import { useBillingText } from '../billingText';

/** An order's amount in the orders overview (spec: Billing screens): the total and its state. */
export function BillingAmount({
  billing,
  className,
}: {
  billing: BillingSummaryResponse;
  /** Defaults to end-aligned, as in the table's amount column. */
  className?: string;
}) {
  const { t } = useTranslation('billing');
  const total = useBillingText().total(billing);

  return (
    <span className={cn('inline-flex flex-wrap items-center justify-end gap-2', className)}>
      <span className={total === null ? 'text-muted-foreground' : 'tabular-nums'}>
        {total ?? t('overview.priceMissing')}
      </span>
      <StatusBadge kind="billing" value={billing.state} />
    </span>
  );
}
