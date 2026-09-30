import i18next, { type i18n as I18n } from 'i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import { initReactI18next } from 'react-i18next';
import {
  DEFAULT_LANGUAGE,
  LANGUAGE_STORAGE_KEY,
  matchLanguage,
  SUPPORTED_LANGUAGES,
  type Language,
} from './config';
import caCommon from './locales/ca-ES-valencia/common.json';
import enCommon from './locales/en/common.json';
import esCommon from './locales/es-ES/common.json';

export const DEFAULT_NAMESPACE = 'common';

export const resources = {
  'es-ES': { common: esCommon },
  'ca-ES-valencia': { common: caCommon },
  en: { common: enCommon },
} as const satisfies Record<Language, { common: object }>;

/**
 * Creates and initialises an i18next instance: remembered choice first, then the browser's
 * preferred languages mapped by primary subtag, then Spanish. `<html lang>` always follows the
 * active language (UC-27, ADR-0007).
 */
export async function createI18n(): Promise<I18n> {
  const instance = i18next.createInstance();
  instance.on('languageChanged', (language) => {
    document.documentElement.lang = language;
  });

  await instance
    .use(LanguageDetector)
    .use(initReactI18next)
    .init({
      resources,
      supportedLngs: [...SUPPORTED_LANGUAGES],
      fallbackLng: DEFAULT_LANGUAGE,
      load: 'currentOnly',
      ns: [DEFAULT_NAMESPACE],
      defaultNS: DEFAULT_NAMESPACE,
      interpolation: { escapeValue: false }, // React already escapes rendered values.
      returnNull: false,
      detection: {
        order: ['localStorage', 'navigator'],
        lookupLocalStorage: LANGUAGE_STORAGE_KEY,
        caches: [], // Only an explicit choice is stored (rememberLanguage).
        convertDetectedLanguage: (language: string) => matchLanguage(language) ?? language,
      },
    });

  return instance;
}
