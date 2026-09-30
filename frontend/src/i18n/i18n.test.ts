import { afterEach, describe, expect, it, vi } from 'vitest';
import { DEFAULT_LANGUAGE, LANGUAGE_STORAGE_KEY, matchLanguage, SUPPORTED_LANGUAGES } from './config';
import { createI18n } from './index';

function browserPrefers(...languages: string[]): void {
  vi.spyOn(navigator, 'languages', 'get').mockReturnValue(languages);
  vi.spyOn(navigator, 'language', 'get').mockReturnValue(languages[0] ?? '');
}

describe('matchLanguage', () => {
  it.each([
    ['es-ES', 'es-ES'],
    ['es', 'es-ES'],
    ['es-MX', 'es-ES'],
    ['ca', 'ca-ES-valencia'],
    ['ca-ES', 'ca-ES-valencia'],
    ['ca-ES-valencia', 'ca-ES-valencia'],
    ['en', 'en'],
    ['en-US', 'en'],
    ['EN-gb', 'en'],
  ])('maps %s to %s by primary subtag', (tag, expected) => {
    expect(matchLanguage(tag)).toBe(expected);
  });

  it.each(['fr-FR', 'de', '', 'cat'])('returns undefined for unsupported tag "%s"', (tag) => {
    expect(matchLanguage(tag)).toBeUndefined();
  });
});

describe('createI18n', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('supports exactly the three UI languages with Spanish as default', () => {
    expect(SUPPORTED_LANGUAGES).toEqual(['es-ES', 'ca-ES-valencia', 'en']);
    expect(DEFAULT_LANGUAGE).toBe('es-ES');
  });

  it('uses the browser language when it is supported', async () => {
    browserPrefers('en-US', 'en');

    const i18n = await createI18n();

    expect(i18n.language).toBe('en');
  });

  it('maps a generic Catalan browser to Valencian', async () => {
    browserPrefers('ca-ES');

    const i18n = await createI18n();

    expect(i18n.language).toBe('ca-ES-valencia');
  });

  it('uses the first supported language in the browser preference list', async () => {
    browserPrefers('fr-FR', 'en-GB', 'es-ES');

    const i18n = await createI18n();

    expect(i18n.language).toBe('en');
  });

  it('falls back to Spanish when no browser language is supported', async () => {
    browserPrefers('fr-FR');

    const i18n = await createI18n();

    expect(i18n.language).toBe('es-ES');
  });

  it('prefers the remembered choice over the browser language', async () => {
    browserPrefers('en-US');
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, 'ca-ES-valencia');

    const i18n = await createI18n();

    expect(i18n.language).toBe('ca-ES-valencia');
  });

  it('does not remember a language the user never chose', async () => {
    browserPrefers('en-US');

    await createI18n();

    expect(window.localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBeNull();
  });

  it('ignores an unsupported remembered value', async () => {
    browserPrefers('en-US');
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, 'fr-FR');

    const i18n = await createI18n();

    expect(i18n.language).toBe('en');
  });

  it('sets the document language from the detected language on start', async () => {
    browserPrefers('en-US');
    document.documentElement.lang = 'es-ES';

    await createI18n();

    expect(document.documentElement.lang).toBe('en');
  });

  it('keeps the document language equal to the active language', async () => {
    browserPrefers('es-ES');
    const i18n = await createI18n();
    expect(document.documentElement.lang).toBe('es-ES');

    await i18n.changeLanguage('ca-ES-valencia');

    expect(document.documentElement.lang).toBe('ca-ES-valencia');
  });

  it('translates common texts in every language', async () => {
    browserPrefers('es-ES');
    const i18n = await createI18n();

    const titles = [];
    for (const language of SUPPORTED_LANGUAGES) {
      await i18n.changeLanguage(language);
      titles.push(i18n.t('notFound.title'));
    }

    expect(new Set(titles).size).toBe(3);
  });
});
