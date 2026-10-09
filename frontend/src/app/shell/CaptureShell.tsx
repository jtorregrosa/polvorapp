import { useRef } from 'react';
import { Outlet } from 'react-router';
import { PublicLayout } from '@/components/app/PublicLayout';
import { SaveNoticeProvider } from '@/components/app/SaveNotice';
import { useFocusMainOnNavigation } from './useFocusMainOnNavigation';
import { VersionFooter } from './VersionFooter';

/**
 * Route element for the distribution capture screen (add-offline-distribution-capture D4): the public
 * layout at the page's width, outside the signed-in shell, so it opens without connectivity and
 * without a live session. No navigation: its links lead back when the network allows.
 */
export function CaptureShell() {
  const main = useRef<HTMLElement>(null);
  useFocusMainOnNavigation(main);

  return (
    <SaveNoticeProvider>
      <PublicLayout wide footer={<VersionFooter />} mainRef={main}>
        <Outlet />
      </PublicLayout>
    </SaveNoticeProvider>
  );
}
