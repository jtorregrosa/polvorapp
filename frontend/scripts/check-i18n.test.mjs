import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { findTranslationProblems } from './check-i18n.mjs';

const roots = [];

function localesRoot(files) {
  const root = mkdtempSync(join(tmpdir(), 'i18n-'));
  roots.push(root);
  for (const [path, content] of Object.entries(files)) {
    const [locale] = path.split('/');
    mkdirSync(join(root, locale), { recursive: true });
    writeFileSync(join(root, path), JSON.stringify(content));
  }
  return root;
}

const complete = {
  'es-ES/common.json': { app: { name: 'PolvorApp' }, home: { title: 'Bienvenida' } },
  'ca-ES-valencia/common.json': { app: { name: 'PolvorApp' }, home: { title: 'Benvinguda' } },
  'en/common.json': { app: { name: 'PolvorApp' }, home: { title: 'Welcome' } },
};

describe('findTranslationProblems', () => {
  afterEach(() => {
    for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true });
  });

  it('accepts locales that share every key with non-empty values', () => {
    expect(findTranslationProblems(localesRoot(complete))).toEqual([]);
  });

  it('reports a key missing in one locale, naming the locale and the key', () => {
    const root = localesRoot({
      ...complete,
      'ca-ES-valencia/common.json': { app: { name: 'PolvorApp' } },
    });

    expect(findTranslationProblems(root)).toEqual(['ca-ES-valencia/common.json: missing key "home.title"']);
  });

  it('reports a key that exists only in one locale as extra', () => {
    const root = localesRoot({
      ...complete,
      'en/common.json': { app: { name: 'PolvorApp' }, home: { title: 'Welcome', subtitle: 'Hi' } },
    });

    const problems = findTranslationProblems(root);

    expect(problems).toContain('es-ES/common.json: missing key "home.subtitle"');
    expect(problems).toContain('ca-ES-valencia/common.json: missing key "home.subtitle"');
  });

  it('reports empty values', () => {
    const root = localesRoot({
      ...complete,
      'en/common.json': { app: { name: 'PolvorApp' }, home: { title: '  ' } },
    });

    expect(findTranslationProblems(root)).toEqual(['en/common.json: empty value for "home.title"']);
  });

  it('reports a translation whose interpolation placeholders differ', () => {
    const withPlaceholder = (text) => ({ footer: { version: text } });
    const root = localesRoot({
      'es-ES/common.json': withPlaceholder('Versión {{version}}'),
      'ca-ES-valencia/common.json': withPlaceholder('Versió {{versio}}'),
      'en/common.json': withPlaceholder('Version'),
    });

    const problems = findTranslationProblems(root);

    expect(problems).toContain(
      'ca-ES-valencia/common.json: placeholders of "footer.version" differ ({{versio}} vs {{version}})',
    );
    expect(problems).toContain(
      'en/common.json: placeholders of "footer.version" differ (none vs {{version}})',
    );
  });

  it('reports a namespace file missing in one locale', () => {
    const root = localesRoot({ ...complete, 'es-ES/registry.json': { title: 'Registro' } });

    const problems = findTranslationProblems(root);

    expect(problems).toContain('ca-ES-valencia/registry.json: missing namespace file');
    expect(problems).toContain('en/registry.json: missing namespace file');
  });

  it('requires all three supported locales', () => {
    const root = localesRoot({
      'es-ES/common.json': complete['es-ES/common.json'],
      'en/common.json': complete['en/common.json'],
    });

    expect(findTranslationProblems(root)).toContain('ca-ES-valencia: missing locale directory');
  });
});
