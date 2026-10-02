import { useTranslation } from 'react-i18next';
import type { EditionResponse, EditionRowResponse } from '@/api/generated/model';
import { useEditionDates } from './dates';

/** What the next order window date is, in words, or that none is pending. */
export function useNextWindowText(): (edition: Pick<EditionResponse, 'nextWindow'>) => string {
  const { t } = useTranslation('editions');
  const dates = useEditionDates();
  return (edition) =>
    edition.nextWindow
      ? t(`detail.window.${edition.nextWindow.kind}`, { date: dates.day(edition.nextWindow.date) })
      : t('detail.nextWindowNone');
}

/** Whether FiringChiefs may edit orders now (BR-10): only while the current edition's orders are open. */
export function useOrdersText(): (edition: Pick<EditionResponse, 'status' | 'ordersOpen'>) => string {
  const { t } = useTranslation('editions');
  return (edition) =>
    edition.status === 'IN_PROGRESS' && edition.ordersOpen
      ? t('detail.ordersEditable')
      : t('detail.ordersReadOnly');
}

/** The edition a new one of `year` copies from: the latest with an earlier year (design D5). */
export function copySource(
  editions: readonly EditionRowResponse[],
  year: number | undefined,
): number | undefined {
  const earlier = editions
    .filter((edition) => year === undefined || edition.year < year)
    .map((edition) => edition.year);
  return earlier.length > 0 ? Math.max(...earlier) : undefined;
}
