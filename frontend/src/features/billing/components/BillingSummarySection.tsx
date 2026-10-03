import { useTranslation } from 'react-i18next';
import type { BillingSummaryResponse } from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { AmountTable } from '@/components/app/AmountTable';
import { SectionCard } from '@/components/app/SectionCard';
import { StatusBadge } from '@/components/app/StatusBadge';
import { useBillingText } from '../billingText';

export interface BillingSummarySectionProps {
  billing: BillingSummaryResponse;
  /** One order's summary, or the edition billing of every prepared order (Admins). */
  scope?: 'order' | 'edition';
}

/**
 * What a comparsa owes for its order, or every order of the edition owes (spec: Billing screens):
 * the four lines and the total, whether it is provisional or final and why, and the missing prices
 * in place of the total.
 */
export function BillingSummarySection({ billing, scope = 'order' }: BillingSummarySectionProps) {
  const { t } = useTranslation('billing');
  const text = useBillingText();
  const copy = scope === 'order' ? 'section' : 'edition';
  const title = t(`${copy}.title`);
  const missingPrices = text.missingPrices(billing);
  const note = billing.state === 'FINAL' ? t(`${copy}.finalNote`) : t(`${copy}.provisionalNote`);

  return (
    <SectionCard
      title={title}
      description={t(`${copy}.description`)}
      action={<StatusBadge kind="billing" value={billing.state} />}
      span="full"
    >
      <p className="text-sm text-muted-foreground">{note}</p>
      <AmountTable title={title} rows={text.rows(billing)} total={text.total(billing)} />
      {missingPrices !== null && (
        <AlertBanner severity="warning" live={false}>
          {missingPrices} {t(`${copy}.noTotal`)}
        </AlertBanner>
      )}
    </SectionCard>
  );
}
