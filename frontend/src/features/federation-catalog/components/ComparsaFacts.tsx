import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { KeyFacts, type KeyFact } from '@/components/app/KeyFacts';
import { StatusBadge } from '@/components/app/StatusBadge';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { useFormatters } from '@/lib/format';
import { useComparsaFigures } from '../comparsaFigures';

const LINK = 'underline underline-offset-4 hover:no-underline';

/**
 * A comparsa's figures under its header (UI audit, item 17): its active and reserve arquebusiers,
 * the active ones with warnings and its order of the current edition, each leading to its list or
 * page. Nothing while they load; a request that failed is said, with a retry, so a missing order
 * never reads as "no edition in progress".
 */
export function ComparsaFacts({ comparsaId }: { comparsaId: string }) {
  const { t } = useTranslation('catalog');
  const { number } = useFormatters();
  const { figures: all, failure } = useComparsaFigures();
  const failed = failure && (
    <LoadFailure error={failure.error} consequence={t('comparsas.figuresFailed')} onRetry={failure.retry} />
  );
  if (!all) return failed ?? null;
  const figures = all.byComparsa.get(comparsaId) ?? { active: 0, reserve: 0, activeWithWarnings: 0 };
  const list = `/arquebusiers?comparsaId=${comparsaId}`;
  const counted = (id: string, label: string, value: number, to: string): KeyFact => ({
    id,
    label,
    value: (
      <Link to={to} className={LINK} aria-label={t('comparsas.facts.link', { value: number(value), label })}>
        {number(value)}
      </Link>
    ),
  });
  const order = figures.order;
  const items: KeyFact[] = [
    counted('active', t('comparsas.facts.active'), figures.active, `${list}&status=ACTIVE`),
    counted('reserve', t('comparsas.facts.reserve'), figures.reserve, `${list}&status=RESERVE`),
    counted(
      'warnings',
      t('comparsas.facts.withWarnings'),
      figures.activeWithWarnings,
      `${list}&status=ACTIVE&warning=ANY`,
    ),
    ...(order && all.year !== undefined
      ? [
          {
            id: 'order',
            label: t('comparsas.facts.order', { year: all.year }),
            value: (
              <span className="flex flex-wrap items-center gap-2">
                {order.status ? (
                  <StatusBadge kind="order" value={order.status} />
                ) : (
                  t('comparsas.facts.notPrepared')
                )}
                {order.orderId && (
                  <Link to={`/orders/${order.orderId}`} className={`text-help ${LINK}`}>
                    {t('comparsas.facts.openOrder')}
                  </Link>
                )}
              </span>
            ),
          },
        ]
      : []),
  ];
  return (
    <>
      {failed}
      <KeyFacts label={t('comparsas.facts.label')} items={items} />
    </>
  );
}
