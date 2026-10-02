import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useGetCurrentEdition } from '@/api/generated/editions/editions';
import type { CurrentEditionResponse } from '@/api/generated/model';
import { SectionCard } from '@/components/app/SectionCard';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { useSession } from '@/features/identity-access/session';
import { todayIso } from '@/lib/dates';
import { useEditionDates } from '../dates';
import { EditionBadges } from '../pages/EditionsPage';
import { useNextWindowText, useOrdersText } from '../editionText';

/** How many upcoming milestones the card lists (spec: Current edition on the start page). */
const UPCOMING_MILESTONES = 3;

/**
 * Spec "Current edition on the start page": the edition in progress with whether its orders are
 * open, the next order window date and the next milestones; or that none is in progress. Its own
 * failure never hides the rest of the start page.
 */
export function CurrentEditionCard() {
  const { t } = useTranslation('editions');
  const session = useSession();
  const isAdmin = session.account?.role === 'ADMIN';
  const current = useGetCurrentEdition({ query: { enabled: session.status === 'signedIn' } });
  const dates = useEditionDates();
  const nextWindow = useNextWindowText();
  const orders = useOrdersText();
  const edition = (current.data?.data as CurrentEditionResponse | undefined)?.edition;

  if (current.isError) {
    return (
      <LoadFailure
        error={current.error}
        consequence={t('currentCard.loadFailed')}
        onRetry={() => current.refetch()}
      />
    );
  }
  if (!current.isSuccess) {
    return null;
  }
  if (!edition) {
    return (
      <SectionCard title={t('currentCard.title')}>
        <p className="text-body font-semibold text-foreground">{t('currentCard.noneTitle')}</p>
        <p className="text-body text-muted-foreground">{t('currentCard.noneDescription')}</p>
        {isAdmin && (
          <Link
            to="/editions"
            className="text-body font-semibold text-foreground underline underline-offset-4"
          >
            {t('currentCard.toEditions')}
          </Link>
        )}
      </SectionCard>
    );
  }

  const today = todayIso();
  const upcoming = edition.milestones
    .filter((milestone) => milestone.date >= today)
    .slice(0, UPCOMING_MILESTONES);

  return (
    <SectionCard title={t('currentCard.title')}>
      <div className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
          <Link
            to={`/editions/${edition.id}`}
            className="font-display text-section text-foreground underline underline-offset-4"
          >
            {t('currentCard.name', { year: edition.year })}
          </Link>
          <EditionBadges edition={edition} />
        </div>
        <p className="text-body text-foreground">{orders(edition)}</p>
        <p className="text-body text-foreground">{nextWindow(edition)}</p>
        <div className="flex flex-col gap-1.5">
          <h3 className="text-label text-foreground">{t('currentCard.milestones')}</h3>
          {upcoming.length === 0 ? (
            <p className="text-body text-muted-foreground">{t('currentCard.noMilestones')}</p>
          ) : (
            <ul className="flex flex-col gap-1 text-body">
              {upcoming.map((milestone) => (
                <li key={milestone.id}>
                  <span className="text-muted-foreground">{dates.day(milestone.date)}</span>
                  <span aria-hidden="true"> · </span>
                  {milestone.title}
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </SectionCard>
  );
}
