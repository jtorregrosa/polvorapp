import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';

/** Sets `<title>` to "<page> · PolvorApp" in the active language (WCAG 2.4.2). */
export function useDocumentTitle(pageTitle: string): void {
  const { t } = useTranslation();
  const appName = t('app.name');

  useEffect(() => {
    document.title = `${pageTitle} · ${appName}`;
  }, [pageTitle, appName]);
}
