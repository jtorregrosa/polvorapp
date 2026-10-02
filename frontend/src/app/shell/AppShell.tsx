import { useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Outlet } from 'react-router';
import { AlertBanner } from '@/components/app/AlertBanner';
import { AppLayout, type NavigationItem } from '@/components/app/AppLayout';
import { UserMenu } from '@/components/app/UserMenu';
import { useWarningCount } from '@/features/compliance-insights/components/useWarningCount';
import { useFiringChiefComparsaCards } from '@/features/federation-catalog/components/useFiringChiefComparsaCards';
import { useSaveLanguage, useSession, useSignOut } from '@/features/identity-access/session';
import { NAVIGATION } from '../navigation';
import { useFocusMainOnNavigation } from './useFocusMainOnNavigation';
import { VersionFooter } from './VersionFooter';

type ShellProblem = 'signOutFailed' | 'languageNotSaved';

/**
 * Route element for every signed-in page: the design-system layout with the navigation allowed
 * for the user's role, a FiringChief's comparsa cards, the user menu, and the language switch saved
 * as the user's preference (platform spec: Application shell; identity-access spec: Switch UI
 * language).
 */
export function AppShell() {
  const { t } = useTranslation();
  const { t: tIdentity } = useTranslation('identity');
  const main = useRef<HTMLElement>(null);
  useFocusMainOnNavigation(main);
  const session = useSession();
  const signOut = useSignOut();
  const saveLanguage = useSaveLanguage();
  const [problem, setProblem] = useState<ShellProblem>();
  const latestAttempt = useRef(0);
  const role = session.account?.role;
  const comparsaCards = useFiringChiefComparsaCards();
  const warningCount = useWarningCount();

  const navigation = useMemo(
    () =>
      NAVIGATION.filter((entry) => !entry.roles || (role !== undefined && entry.roles.includes(role))).map(
        ({ to, labelKey, icon, count }): NavigationItem => {
          const item = { to, icon, label: t(labelKey) };
          return count === 'warnings' && warningCount
            ? { ...item, count: warningCount, countLabel: t('nav.warningCount', { count: warningCount }) }
            : item;
        },
      ),
    [t, role, warningCount],
  );

  /** Runs a shell action and shows its problem, if any, above the page; only the latest counts. */
  const attempt = (action: () => Promise<boolean>, failure: ShellProblem): void => {
    const current = ++latestAttempt.current;
    setProblem(undefined);
    const settle = (succeeded: boolean): void => {
      if (current === latestAttempt.current) {
        setProblem(succeeded ? undefined : failure);
      }
    };
    action().then(settle, () => {
      settle(false);
    });
  };

  const userMenu = session.account && (
    <UserMenu
      name={session.account.name}
      roleLabel={tIdentity(`roles.${session.account.role}`)}
      accountHref="/account"
      onSignOut={() => {
        attempt(signOut, 'signOutFailed');
      }}
      onLanguageChange={(language) => {
        attempt(() => saveLanguage(language), 'languageNotSaved');
      }}
    />
  );

  return (
    <AppLayout
      navigation={navigation}
      sidebarCards={comparsaCards}
      sidebarFooter={<VersionFooter />}
      userMenu={userMenu}
      mainRef={main}
    >
      {problem && <AlertBanner severity="error">{t(`session.${problem}`)}</AlertBanner>}
      <Outlet />
    </AppLayout>
  );
}
