#!/usr/bin/env node
// Translation completeness check (spec: Translation completeness; NFR-02, ADR-0007).
// Every locale must have the same namespace files and the same keys, with non-empty values.
// Usage: node scripts/check-i18n.mjs <locales directory>
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const SUPPORTED_LOCALES = ['es-ES', 'ca-ES-valencia', 'en'];

/** Flattens nested translation objects into dotted keys: { a: { b: 'x' } } -> { 'a.b': 'x' }. */
function flatten(object, prefix = '') {
  return Object.entries(object).flatMap(([key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    return value !== null && typeof value === 'object' ? flatten(value, path) : [[path, value]];
  });
}

const placeholders = (text) => [...new Set(text.match(/{{\s*[^}]+?\s*}}/g) ?? [])].sort().join(' ');

function namespaces(localeDirectory) {
  return readdirSync(localeDirectory).filter((file) => file.endsWith('.json'));
}

/** Returns human-readable problems; an empty array means the translations are complete. */
export function findTranslationProblems(root) {
  const problems = [];
  const present = SUPPORTED_LOCALES.filter((locale) => {
    const directory = join(root, locale);
    const exists = existsSync(directory) && statSync(directory).isDirectory();
    if (!exists) problems.push(`${locale}: missing locale directory`);
    return exists;
  });

  const allNamespaces = [...new Set(present.flatMap((locale) => namespaces(join(root, locale))))].sort();
  for (const namespace of allNamespaces) {
    const entriesByLocale = new Map();
    for (const locale of present) {
      const file = join(root, locale, namespace);
      if (!existsSync(file)) {
        problems.push(`${locale}/${namespace}: missing namespace file`);
        continue;
      }
      entriesByLocale.set(locale, new Map(flatten(JSON.parse(readFileSync(file, 'utf8')))));
    }

    const allKeys = [
      ...new Set([...entriesByLocale.values()].flatMap((entries) => [...entries.keys()])),
    ].sort();
    // The first supported locale (es-ES) is the reference for interpolation placeholders.
    const [reference] = [...entriesByLocale.values()];
    for (const [locale, entries] of entriesByLocale) {
      for (const key of allKeys) {
        const value = entries.get(key);
        if (!entries.has(key)) {
          problems.push(`${locale}/${namespace}: missing key "${key}"`);
        } else if (typeof value !== 'string' || value.trim() === '') {
          problems.push(`${locale}/${namespace}: empty value for "${key}"`);
        } else if (typeof reference?.get(key) === 'string') {
          const expected = placeholders(reference.get(key));
          const actual = placeholders(value);
          if (actual !== expected) {
            problems.push(
              `${locale}/${namespace}: placeholders of "${key}" differ (${actual || 'none'} vs ${expected || 'none'})`,
            );
          }
        }
      }
    }
  }
  return problems;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = process.argv[2];
  if (!root) {
    console.error('Usage: node scripts/check-i18n.mjs <locales directory>');
    process.exit(2);
  }
  const problems = findTranslationProblems(root);
  if (problems.length > 0) {
    console.error(`Translation check failed (${problems.length} problems):`);
    for (const problem of problems) console.error(`  - ${problem}`);
    process.exit(1);
  }
  console.log(`Translations complete for ${SUPPORTED_LOCALES.join(', ')}.`);
}
