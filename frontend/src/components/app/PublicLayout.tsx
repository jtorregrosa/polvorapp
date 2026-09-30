import type { ReactNode, RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import { LanguageSwitcher } from './LanguageSwitcher';
import { PolvorAppMark } from './PolvorAppMark';
import { ThemeSwitcher } from './ThemeSwitcher';

export interface PublicLayoutProps {
  /** Under the card, e.g. the API version. */
  footer?: ReactNode;
  mainRef?: RefObject<HTMLElement | null>;
  children: ReactNode;
}

/**
 * Layout of the pages used before signing in (platform spec: Application shell): the PolvorApp
 * mark, the language and theme switchers, and one centred card. No navigation.
 */
export function PublicLayout({ footer, mainRef, children }: PublicLayoutProps) {
  const { t } = useTranslation();

  return (
    <div className="flex min-h-svh flex-col bg-muted/40">
      <a
        href="#main"
        className="sr-only z-50 rounded-md bg-background px-4 py-2 text-foreground focus:not-sr-only focus:fixed focus:top-4 focus:left-4"
      >
        {t('shell.skipToContent')}
      </a>
      <header className="flex flex-wrap items-center gap-2 px-4 py-3 sm:px-6">
        <span className="flex flex-1 items-center gap-2 text-lg font-semibold">
          <PolvorAppMark />
          <span>{t('app.name')}</span>
        </span>
        <LanguageSwitcher />
        <ThemeSwitcher />
      </header>
      <main
        id="main"
        ref={mainRef}
        tabIndex={-1}
        className="flex flex-1 items-start justify-center px-4 py-6 outline-none sm:items-center sm:py-12"
      >
        <div className="w-full max-w-md rounded-lg border bg-card p-6 text-card-foreground shadow-sm sm:p-8">
          {children}
        </div>
      </main>
      {footer && <footer className="px-4 py-3 text-center text-xs text-muted-foreground">{footer}</footer>}
    </div>
  );
}
