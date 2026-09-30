import { useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { Outlet } from 'react-router';
import { LanguageSwitcher } from '@/components/app/LanguageSwitcher';
import { useFocusMainOnNavigation } from './useFocusMainOnNavigation';
import { VersionFooter } from './VersionFooter';
import './shell.css';

/**
 * Layout of every page (spec: Application shell): skip link, header with the application name
 * and language switcher, main content and footer with the API version.
 */
export function AppShell() {
  const { t } = useTranslation();
  const main = useRef<HTMLElement>(null);
  useFocusMainOnNavigation(main);

  return (
    <div className="shell">
      <a className="skip-link" href="#main">
        {t('shell.skipToContent')}
      </a>
      <header className="shell-header">
        <p className="shell-app-name">{t('app.name')}</p>
        <LanguageSwitcher />
      </header>
      <main id="main" ref={main} className="shell-main" tabIndex={-1}>
        <Outlet />
      </main>
      <footer className="shell-footer">
        <VersionFooter />
      </footer>
    </div>
  );
}
