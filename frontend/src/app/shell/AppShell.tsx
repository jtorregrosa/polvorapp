import { useMemo, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { Outlet } from 'react-router';
import { AppLayout } from '@/components/app/AppLayout';
import { NAVIGATION } from '../navigation';
import { useFocusMainOnNavigation } from './useFocusMainOnNavigation';
import { VersionFooter } from './VersionFooter';

/** Route element for every page: the design-system layout with the app's navigation. */
export function AppShell() {
  const { t } = useTranslation();
  const main = useRef<HTMLElement>(null);
  useFocusMainOnNavigation(main);

  const navigation = useMemo(
    () => NAVIGATION.map(({ to, labelKey, icon }) => ({ to, icon, label: t(labelKey) })),
    [t],
  );

  return (
    <AppLayout navigation={navigation} sidebarFooter={<VersionFooter />} mainRef={main}>
      <Outlet />
    </AppLayout>
  );
}
