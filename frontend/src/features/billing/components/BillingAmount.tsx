import { useTranslation } from 'react-i18next';
import type { BillingSummaryResponse } from '@/api/generated/model';
import { StatusBadge } from '@/components/app/StatusBadge';
import { cn } from '@/lib/cn';
import { useBillingText } from '../billingText';

/** An order's total, or that a price is missing (spec: Billing screens). */
export function BillingTotal({ billing }: { billing: BillingSummaryResponse }) {
  const { t } = useTranslation('billing');
  const total = useBillingText().total(billing);
  return (
    <span className={total === null ? 'text-muted-foreground' : 'tabular-nums'}>
      {total ?? t('overview.priceMissing')}
    </span>
  );
}

/**
 * An order's amount where there are no columns (the orders overview on phones): the total and its
 * state, provisional or final.
 */
export function BillingAmount({
  billing,
  className,
}: {
  billing: BillingSummaryResponse;
  className?: string;
}) {
  return (
    <span className={cn('inline-flex flex-wrap items-center gap-2', className)}>
      <BillingTotal billing={billing} />
      <StatusBadge kind="billing" value={billing.state} />
    </span>
  );
}
