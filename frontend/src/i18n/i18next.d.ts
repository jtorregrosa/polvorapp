import 'i18next';
import type { DEFAULT_NAMESPACE, resources } from './index';

// Typed translation keys: `t('unknown.key')` is a compile error.
declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: typeof DEFAULT_NAMESPACE;
    resources: (typeof resources)['es-ES'];
    returnNull: false;
  }
}
