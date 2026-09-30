import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { DEFAULT_LANGUAGE, matchLanguage, type Language } from '@/i18n/config';

/** Festival dates are local to San Vicente del Raspeig. */
const TIME_ZONE = 'Europe/Madrid';

// English uses British conventions (day before month), as the Federation is in Spain.
const INTL_LOCALES: Readonly<Record<Language, string>> = {
  'es-ES': 'es-ES',
  'ca-ES-valencia': 'ca-ES-valencia',
  en: 'en-GB',
};

/** Intl locale used to format dates and numbers for a UI language (NFR-02). */
export function intlLocale(language: string): string {
  return INTL_LOCALES[matchLanguage(language) ?? DEFAULT_LANGUAGE];
}

export function formatDate(date: Date, language: string, options: Intl.DateTimeFormatOptions = {}): string {
  return new Intl.DateTimeFormat(intlLocale(language), { timeZone: TIME_ZONE, ...options }).format(date);
}

export function formatNumber(
  value: number,
  language: string,
  options: Intl.NumberFormatOptions = {},
): string {
  return new Intl.NumberFormat(intlLocale(language), options).format(value);
}

export interface Formatters {
  date: (date: Date, options?: Intl.DateTimeFormatOptions) => string;
  number: (value: number, options?: Intl.NumberFormatOptions) => string;
}

/** Date and number formatters bound to the active UI language. */
export function useFormatters(): Formatters {
  const { i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? i18n.language;
  return useMemo(
    () => ({
      date: (date, options) => formatDate(date, language, options),
      number: (value, options) => formatNumber(value, language, options),
    }),
    [language],
  );
}
