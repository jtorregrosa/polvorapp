/** UI languages (ADR-0007). Stored and sent to the API as these exact tags. */
export const SUPPORTED_LANGUAGES = ['es-ES', 'ca-ES-valencia', 'en'] as const;

export type Language = (typeof SUPPORTED_LANGUAGES)[number];

export const DEFAULT_LANGUAGE: Language = 'es-ES';

/** Browser storage key that remembers the user's choice (UC-27). */
export const LANGUAGE_STORAGE_KEY = 'polvorapp.language';

const BY_PRIMARY_SUBTAG: Readonly<Record<string, Language>> = {
  es: 'es-ES',
  // Valencian is the only Catalan variant offered, so any Catalan preference maps to it.
  ca: 'ca-ES-valencia',
  en: 'en',
};

/**
 * Remembers a language the user explicitly chose (UC-27). Detected languages are never stored,
 * so a first visit keeps following the browser. Storage may be unavailable (private mode or
 * blocked site data); the switch then simply lasts for the session.
 */
export function rememberLanguage(language: Language): void {
  try {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
  } catch {
    // Not remembering is acceptable; the language still changes.
  }
}

/** Maps a BCP 47 tag to a supported language by its primary subtag (`es-MX` → `es-ES`). */
export function matchLanguage(tag: string): Language | undefined {
  const primary = tag.split('-')[0]?.toLowerCase() ?? '';
  return BY_PRIMARY_SUBTAG[primary];
}
