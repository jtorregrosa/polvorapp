import { mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { ESLint } from 'eslint';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';

// Runs the project's real ESLint configuration on probe files, so the guardrails cannot silently
// disappear from eslint.config.js (spec: Translation completeness — hard-coded text).
const probeDirectory = join(import.meta.dirname, '..', 'src', 'features', '__lint_probe__');

async function lint(fileName, source) {
  const filePath = join(probeDirectory, fileName);
  writeFileSync(filePath, source);
  const [result] = await new ESLint({ cwd: join(import.meta.dirname, '..') }).lintFiles([filePath]);
  return result.messages.map((message) => message.ruleId);
}

describe('ESLint guardrails', () => {
  beforeAll(() => mkdirSync(probeDirectory, { recursive: true }));
  afterAll(() => rmSync(probeDirectory, { recursive: true, force: true }));

  it('rejects hard-coded user-facing text in JSX', async () => {
    const rules = await lint('Literal.tsx', 'export function Literal() {\n  return <p>Hola mundo</p>;\n}\n');

    expect(rules).toContain('i18next/no-literal-string');
  });

  it('rejects hard-coded accessible labels', async () => {
    const rules = await lint(
      'Label.tsx',
      'export function Label() {\n  return <button type="button" aria-label="Cerrar" />;\n}\n',
    );

    expect(rules).toContain('i18next/no-literal-string');
  });

  it('accepts translated text', async () => {
    const rules = await lint(
      'Translated.tsx',
      "import { useTranslation } from 'react-i18next';\n\nexport function Translated() {\n  const { t } = useTranslation();\n  return <p>{t('home.title')}</p>;\n}\n",
    );

    expect(rules).toEqual([]);
  });
}, 60_000);
