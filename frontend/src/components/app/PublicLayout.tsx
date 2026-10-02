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
 * mark on a night band, the language and theme switchers, and one centred card. No navigation.
 */
export function PublicLayout({ footer, mainRef, children }: PublicLayoutProps) {
  const { t } = useTranslation();

  return (
    <div className="flex min-h-svh flex-col bg-background">
      <a
        href="#main"
        className="sr-only z-50 rounded-md bg-background px-4 py-2 text-foreground focus:not-sr-only focus:fixed focus:top-4 focus:left-4"
      >
        {t('shell.skipToContent')}
      </a>
      <header className="flex flex-wrap items-center gap-2 px-gutter py-3">
        <span className="flex flex-1 items-center gap-2.5 font-display text-section font-bold text-foreground">
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
        className="flex flex-1 items-start justify-center px-gutter py-section outline-none sm:items-center sm:py-region"
      >
        <div className="flex w-full max-w-md flex-col gap-section rounded-xl border bg-card p-6 text-card-foreground shadow-e2 sm:p-8">
          {children}
        </div>
      </main>
      {footer && (
        <footer className="px-gutter py-3 text-center text-help text-muted-foreground">{footer}</footer>
      )}
    </div>
  );
}
