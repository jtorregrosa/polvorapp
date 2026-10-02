import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { LinkCard } from '@/components/app/LinkCard';
import { PageHeader } from '@/components/app/PageHeader';
import { NAVIGATION } from '@/app/navigation';
import { useSession } from '@/features/identity-access/session';
import { useDocumentTitle } from '@/lib/useDocumentTitle';

/**
 * Start page. Until the dashboard (#7 add-compliance-insights) it offers a shortcut to each section
 * the user's role can open, with what is done there.
 */
export function HomePage() {
  const { t } = useTranslation();
  useDocumentTitle(t('home.title'));
  const role = useSession().account?.role;
  const sections = useMemo(
    () =>
      NAVIGATION.filter(
        (entry) => entry.to !== '/' && (!entry.roles || (role !== undefined && entry.roles.includes(role))),
      ),
    [role],
  );

  return (
    <>
      <PageHeader title={t('home.title')} description={t('home.description')} />
      <section aria-labelledby="home-sections" className="flex flex-col gap-group">
        <h2 id="home-sections" className="text-section text-foreground">
          {t('home.sectionsTitle')}
        </h2>
        <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          {sections.map((entry) => (
            <li key={entry.to} className="flex">
              <LinkCard
                to={entry.to}
                title={t(entry.labelKey)}
                description={t(`home.sections.${entry.to.slice(1)}` as 'home.sections.arquebusiers')}
                icon={entry.icon}
              />
            </li>
          ))}
        </ul>
      </section>
    </>
  );
}
