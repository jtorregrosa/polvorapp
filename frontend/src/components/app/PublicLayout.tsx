import type { ReactNode, RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import { LanguageSwitcher } from './LanguageSwitcher';
import { PolvorAppMark } from './PolvorAppMark';
import { PolvorAppWordmark } from './PolvorAppWordmark';
import { ThemeSwitcher } from './ThemeSwitcher';

export interface PublicLayoutProps {
  /** Under the card, e.g. the API version. */
  footer?: ReactNode;
  mainRef?: RefObject<HTMLElement | null>;
  /**
   * The page's width instead of one centred card, for a page that works without a session, e.g. the
   * distribution capture screen (add-offline-distribution-capture).
   */
  wide?: boolean;
  children: ReactNode;
}

/**
 * Layout of the pages used before signing in (platform spec: Application shell): the PolvorApp
 * logo (mark and wordmark), the language and theme switchers, and one centred card (or the page's
 * width, `wide`). No navigation.
 */
export function PublicLayout({ footer, mainRef, wide = false, children }: PublicLayoutProps) {
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
        <span className="flex flex-1 items-center text-foreground">
          <PolvorAppMark />
          <PolvorAppWordmark label={t('app.name')} />
        </span>
        <LanguageSwitcher />
        <ThemeSwitcher />
      </header>
      <main
        id="main"
        ref={mainRef}
        tabIndex={-1}
        className={
          wide
            ? 'flex flex-1 justify-center px-gutter pt-section pb-region outline-none'
            : 'flex flex-1 items-start justify-center px-gutter py-section outline-none sm:items-center sm:py-region'
        }
      >
        {wide ? (
          <div className="flex w-full max-w-page flex-col gap-section">{children}</div>
        ) : (
          <div className="flex w-full max-w-md flex-col gap-section rounded-xl border bg-card p-6 text-card-foreground shadow-e2 sm:p-8">
            {children}
          </div>
        )}
      </main>
      {footer && (
        <footer className="px-gutter py-3 text-center text-help text-muted-foreground">{footer}</footer>
      )}
    </div>
  );
}
