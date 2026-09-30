import { useRef } from 'react';
import { Outlet } from 'react-router';
import { PublicLayout } from '@/components/app/PublicLayout';
import { useFocusMainOnNavigation } from './useFocusMainOnNavigation';
import { VersionFooter } from './VersionFooter';

/** Route element for the pages used before signing in: the public layout with the API version. */
export function PublicShell() {
  const main = useRef<HTMLElement>(null);
  useFocusMainOnNavigation(main);

  return (
    <PublicLayout footer={<VersionFooter />} mainRef={main}>
      <Outlet />
    </PublicLayout>
  );
}
