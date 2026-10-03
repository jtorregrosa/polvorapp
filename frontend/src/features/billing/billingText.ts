import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import type { BillingLineResponse, BillingSummaryResponse } from '@/api/generated/model';
import type { AmountTableRow } from '@/components/app/AmountTable';
import { useFormatters } from '@/lib/format';

export interface BillingText {
  /** The lines as rows of an `AmountTable`; a missing price stays null, never zero. */
  rows: (summary: BillingSummaryResponse) => AmountTableRow[];
  /** The total as currency, or null when a price is missing. */
  total: (summary: BillingSummaryResponse) => string | null;
  /** Which prices are missing, as a sentence; null when none is. */
  missingPrices: (summary: BillingSummaryResponse) => string | null;
}

/** The words and amounts of a billing summary, in the user's language (spec: Billing screens). */
export function useBillingText(): BillingText {
  const { t } = useTranslation('billing');
  const { number, currency, list } = useFormatters();

  return useMemo(() => {
    const quantity = (line: BillingLineResponse): string =>
      line.concept === 'POWDER'
        ? t('quantity.POWDER', { value: number(line.quantity) })
        : t(`quantity.${line.concept}`, { count: line.quantity });

    return {
      rows: (summary: BillingSummaryResponse): AmountTableRow[] =>
        summary.lines.map((line) => ({
          id: line.concept,
          label: t(`concepts.${line.concept}`),
          quantity: quantity(line),
          unitPrice: line.unitPrice === null ? null : currency(line.unitPrice),
          amount: line.amount === null ? null : currency(line.amount),
        })),
      total: (summary: BillingSummaryResponse): string | null =>
        summary.total === null ? null : currency(summary.total),
      missingPrices: (summary: BillingSummaryResponse): string | null =>
        summary.missingPrices.length === 0
          ? null
          : t('missingPrices', {
              count: summary.missingPrices.length,
              prices: list(summary.missingPrices.map((concept) => t(`prices.${concept}`))),
            }),
    };
  }, [t, number, currency, list]);
}
