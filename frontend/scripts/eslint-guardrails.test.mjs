import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { ESLint } from 'eslint';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';

// Runs the project's real ESLint configuration on probe files, so the guardrails cannot silently
// disappear from eslint.config.js (specs: Translation completeness; Component layers and boundary;
// No values outside the tokens). Probe directories are unique per run (safe in parallel/watch).
const root = join(import.meta.dirname, '..');
const eslint = new ESLint({ cwd: root });
let featureProbes = '';
let compositeProbes = '';

async function lint(directory, fileName, source) {
  const filePath = join(directory, fileName);
  writeFileSync(filePath, source);
  const [result] = await eslint.lintFiles([filePath]);
  const fatal = result.messages.filter((message) => message.fatal);
  if (fatal.length > 0) throw new Error(`Probe did not parse: ${fatal.map((m) => m.message).join('; ')}`);
  return result.messages.map((message) => message.ruleId);
}

const feature = (fileName, source) => lint(featureProbes, fileName, source);
const composite = (fileName, source) => lint(compositeProbes, fileName, source);

const component = (jsx, imports = '') => `${imports}export function Probe() {\n  return ${jsx};\n}\n`;
const withClasses = (className) => component(`<div className="${className}" />`);

describe('ESLint guardrails', () => {
  beforeAll(() => {
    featureProbes = mkdtempSync(join(root, 'src', 'features', '__lint_probe_'));
    compositeProbes = mkdtempSync(join(root, 'src', 'components', 'app', '__lint_probe_'));
  });
  afterAll(() => {
    rmSync(featureProbes, { recursive: true, force: true });
    rmSync(compositeProbes, { recursive: true, force: true });
  });

  describe('translations', () => {
    it('rejects hard-coded user-facing text in JSX', async () => {
      expect(await feature('Literal.tsx', component('<p>Hola mundo</p>'))).toContain(
        'i18next/no-literal-string',
      );
    });

    it('rejects hard-coded accessible labels', async () => {
      expect(await feature('Label.tsx', component('<button type="button" aria-label="Cerrar" />'))).toContain(
        'i18next/no-literal-string',
      );
    });

    it('accepts translated text', async () => {
      const rules = await feature(
        'Translated.tsx',
        "import { useTranslation } from 'react-i18next';\n\nexport function Translated() {\n  const { t } = useTranslation();\n  return <p>{t('home.title')}</p>;\n}\n",
      );

      expect(rules).toEqual([]);
    });
  });

  describe('component layers', () => {
    const importButton = "import { Button } from '@/components/ui/button';\n\n";

    it('rejects a feature importing a primitive', async () => {
      expect(await feature('Primitive.tsx', component('<Button type="button" />', importButton))).toContain(
        'no-restricted-imports',
      );
    });

    it('accepts a composite importing a primitive', async () => {
      expect(
        await composite('UsesPrimitive.tsx', component('<Button type="button" />', importButton)),
      ).toEqual([]);
    });
  });

  describe('design tokens', () => {
    it.each([
      ['arbitrary spacing', 'p-[7px]'],
      ['arbitrary colour', 'text-[#c00]'],
      ['arbitrary property', '[mask-type:luminance]'],
      ['arbitrary opacity', 'bg-red-500/[.5]'],
      ['important arbitrary value', 'mt-[13px]!'],
      ['raw palette colour', 'bg-emerald-600'],
      ['raw palette colour with variant', 'hover:text-red-500'],
      ['raw palette side border', 'border-t-red-500'],
      ['raw palette gradient', 'from-emerald-500'],
      ['raw palette ring offset', 'ring-offset-red-500'],
      ['raw palette drop shadow', 'drop-shadow-red-500'],
      ['raw palette with important prefix', '!bg-red-500'],
      ['raw palette with important suffix', 'bg-red-500!'],
      ['white with opacity', 'bg-white/10'],
      ['opacity on a semantic tone', 'bg-primary/50'],
      ['opacity on a soft tone', 'bg-success-soft/50'],
      ['opacity on a foreground token', 'text-primary-foreground/80'],
      ['opacity on a side border', 'border-t-primary/50'],
      ['opacity on the focus ring', 'ring-ring/50'],
    ])('rejects %s (%s)', async (_, className) => {
      expect(await feature('Rejected.tsx', withClasses(className))).toContain(
        'better-tailwindcss/no-restricted-classes',
      );
    });

    it('rejects unknown classes', async () => {
      expect(await feature('Unknown.tsx', withClasses('text-primray'))).toContain(
        'better-tailwindcss/no-unknown-classes',
      );
    });

    it('rejects inline styles in features, composites and the app shell', async () => {
      const source = component("<div style={{ color: 'red' }} />");

      expect(await feature('Style.tsx', source)).toContain('no-restricted-syntax');
      expect(await composite('Style.tsx', source)).toContain('no-restricted-syntax');
    });

    it('accepts semantic tokens and legitimate bracket or slash syntax', async () => {
      const rules = await feature(
        'Accepted.tsx',
        component(
          '<div className="data-[state=open]:bg-accent size-4 -translate-x-1/2 rounded-md p-4 text-sm ring-offset-2">' +
            '<p className="bg-primary text-primary-foreground" />' +
            '<p className="bg-primary-soft hover:bg-success-soft" />' +
            '<p className="bg-muted/50" />' +
            '</div>',
        ),
      );

      expect(rules).toEqual([]);
    });
  });
}, 120_000);
