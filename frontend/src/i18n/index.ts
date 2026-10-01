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
import caCatalog from './locales/ca-ES-valencia/catalog.json';
import caCommon from './locales/ca-ES-valencia/common.json';
import caIdentity from './locales/ca-ES-valencia/identity.json';
import caRegistry from './locales/ca-ES-valencia/registry.json';
import caUi from './locales/ca-ES-valencia/ui.json';
import enCatalog from './locales/en/catalog.json';
import enCommon from './locales/en/common.json';
import enIdentity from './locales/en/identity.json';
import enRegistry from './locales/en/registry.json';
import enUi from './locales/en/ui.json';
import esCatalog from './locales/es-ES/catalog.json';
import esCommon from './locales/es-ES/common.json';
import esIdentity from './locales/es-ES/identity.json';
import esRegistry from './locales/es-ES/registry.json';
import esUi from './locales/es-ES/ui.json';

export const DEFAULT_NAMESPACE = 'common';

export const resources = {
  'es-ES': { common: esCommon, ui: esUi, identity: esIdentity, catalog: esCatalog, registry: esRegistry },
  'ca-ES-valencia': {
    common: caCommon,
    ui: caUi,
    identity: caIdentity,
    catalog: caCatalog,
    registry: caRegistry,
  },
  en: { common: enCommon, ui: enUi, identity: enIdentity, catalog: enCatalog, registry: enRegistry },
} as const satisfies Record<
  Language,
  { common: object; ui: object; identity: object; catalog: object; registry: object }
>;

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
      ns: [DEFAULT_NAMESPACE, 'ui', 'identity', 'catalog', 'registry'],
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
