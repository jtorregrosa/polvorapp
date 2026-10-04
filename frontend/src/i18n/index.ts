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
import caBilling from './locales/ca-ES-valencia/billing.json';
import caCatalog from './locales/ca-ES-valencia/catalog.json';
import caDistribution from './locales/ca-ES-valencia/distribution.json';
import caExports from './locales/ca-ES-valencia/exports.json';
import caCommon from './locales/ca-ES-valencia/common.json';
import caEditions from './locales/ca-ES-valencia/editions.json';
import caIdentity from './locales/ca-ES-valencia/identity.json';
import caInsights from './locales/ca-ES-valencia/insights.json';
import caNotifications from './locales/ca-ES-valencia/notifications.json';
import caOrders from './locales/ca-ES-valencia/orders.json';
import caRegistry from './locales/ca-ES-valencia/registry.json';
import caUi from './locales/ca-ES-valencia/ui.json';
import enBilling from './locales/en/billing.json';
import enCatalog from './locales/en/catalog.json';
import enDistribution from './locales/en/distribution.json';
import enExports from './locales/en/exports.json';
import enCommon from './locales/en/common.json';
import enEditions from './locales/en/editions.json';
import enIdentity from './locales/en/identity.json';
import enInsights from './locales/en/insights.json';
import enNotifications from './locales/en/notifications.json';
import enOrders from './locales/en/orders.json';
import enRegistry from './locales/en/registry.json';
import enUi from './locales/en/ui.json';
import esBilling from './locales/es-ES/billing.json';
import esCatalog from './locales/es-ES/catalog.json';
import esDistribution from './locales/es-ES/distribution.json';
import esExports from './locales/es-ES/exports.json';
import esCommon from './locales/es-ES/common.json';
import esEditions from './locales/es-ES/editions.json';
import esIdentity from './locales/es-ES/identity.json';
import esInsights from './locales/es-ES/insights.json';
import esNotifications from './locales/es-ES/notifications.json';
import esOrders from './locales/es-ES/orders.json';
import esRegistry from './locales/es-ES/registry.json';
import esUi from './locales/es-ES/ui.json';

export const DEFAULT_NAMESPACE = 'common';

export const resources = {
  'es-ES': {
    common: esCommon,
    ui: esUi,
    identity: esIdentity,
    catalog: esCatalog,
    registry: esRegistry,
    insights: esInsights,
    editions: esEditions,
    orders: esOrders,
    billing: esBilling,
    exports: esExports,
    distribution: esDistribution,
    notifications: esNotifications,
  },
  'ca-ES-valencia': {
    common: caCommon,
    ui: caUi,
    identity: caIdentity,
    catalog: caCatalog,
    registry: caRegistry,
    insights: caInsights,
    editions: caEditions,
    orders: caOrders,
    billing: caBilling,
    exports: caExports,
    distribution: caDistribution,
    notifications: caNotifications,
  },
  en: {
    common: enCommon,
    ui: enUi,
    identity: enIdentity,
    catalog: enCatalog,
    registry: enRegistry,
    insights: enInsights,
    editions: enEditions,
    orders: enOrders,
    billing: enBilling,
    exports: enExports,
    distribution: enDistribution,
    notifications: enNotifications,
  },
} as const satisfies Record<
  Language,
  {
    common: object;
    ui: object;
    identity: object;
    catalog: object;
    registry: object;
    insights: object;
    editions: object;
    orders: object;
    billing: object;
    exports: object;
    distribution: object;
    notifications: object;
  }
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
      ns: [DEFAULT_NAMESPACE, 'ui', 'identity', 'catalog', 'registry', 'insights'],
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
