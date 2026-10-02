/// <reference types="node" />
import { composeStories, composeStory, setProjectAnnotations } from '@storybook/react-vite';
import { cleanup, render, screen } from '@testing-library/react';
import { readdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SUPPORTED_LANGUAGES } from '@/i18n/config';
import { axeViolations } from '@/test/axe';
import preview from '../../../.storybook/preview';

// Spec: Component catalogue. Every composite has stories, and every story renders in the three UI
// languages and both themes without automatically detectable WCAG 2.2 A/AA violations. jsdom has
// no layout, so axe cannot measure colour contrast here: contrast is verified on the tokens
// (src/styles/contrast.test.ts) and in the browser by the Playwright axe checks.

const THEMES = ['light', 'dark'] as const;

/** Files in this folder that are not composites and need no stories (add helpers here). */
const NOT_COMPOSITES = new Set([
  'status.ts',
  'navigation-match.ts',
  'confirm-failure.ts',
  'photo-image.ts',
  'file-rules.ts',
  'use-app-form.ts',
  'save-notice.ts',
]);

/** A text that is a translation key left untranslated, e.g. `status.license.VALID`. */
const RAW_KEY = /^[a-z][A-Za-z]*(?:\.[A-Za-z0-9_]+)+$/;

/** Rendered texts that look like untranslated keys. */
function untranslatedTexts(root: Node): string[] {
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const found: string[] = [];
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const text = node.textContent?.trim() ?? '';
    if (RAW_KEY.test(text)) found.push(text);
  }
  return found;
}

const SOURCE = /\.tsx?$/;
const NOT_SOURCE = /\.(test|stories)\.tsx?$/;

/**
 * Catalogue mismatches in a folder listing: composites without a sibling `*.stories.tsx`, and
 * stories files without a composite.
 */
function catalogueMismatches(files: readonly string[]): string[] {
  const names = new Set(files);
  const composites = files.filter(
    (file) => SOURCE.test(file) && !NOT_SOURCE.test(file) && !NOT_COMPOSITES.has(file),
  );
  const missingStories = composites
    .filter((file) => !names.has(file.replace(SOURCE, '.stories.tsx')))
    .map((file) => `${file}: no stories (add ${file.replace(SOURCE, '.stories.tsx')})`);
  const orphanStories = files
    .filter((file) => file.endsWith('.stories.tsx'))
    .filter((file) => !names.has(file.replace('.stories.tsx', '.tsx')))
    .map((file) => `${file}: no composite`);
  return [...missingStories, ...orphanStories];
}

type StoryModule = Parameters<typeof composeStories>[0];
type ComposedStory = ReturnType<typeof composeStory>;

setProjectAnnotations(preview);

const modules = import.meta.glob<StoryModule>('./*.stories.tsx', { eager: true });

describe('component catalogue', () => {
  afterEach(() => {
    cleanup();
    document.documentElement.classList.remove('dark');
    window.localStorage.clear();
  });

  it('has stories for every composite and a composite for every stories file', () => {
    const entries = readdirSync(dirname(fileURLToPath(import.meta.url)), { withFileTypes: true });

    // Composites live flat in this folder; a subfolder would escape the checks below.
    // (scripts/eslint-guardrails.test.mjs creates temporary `__lint_probe_*` folders here.)
    const own = entries.filter((entry) => !entry.name.startsWith('__lint_probe_'));
    expect(own.filter((entry) => entry.isDirectory()).map((entry) => entry.name)).toEqual([]);
    expect(catalogueMismatches(own.map((entry) => entry.name))).toEqual([]);
  });

  it('reports composites without stories and stories without a composite', () => {
    const files = [
      'Widget.tsx',
      'Widget.test.tsx',
      'Other.tsx',
      'Other.stories.tsx',
      'Orphan.stories.tsx',
      'status.ts',
      'helper.ts',
    ];

    expect(catalogueMismatches(files)).toEqual([
      'Widget.tsx: no stories (add Widget.stories.tsx)',
      'helper.ts: no stories (add helper.stories.tsx)',
      'Orphan.stories.tsx: no composite',
    ]);
  });

  it('finds stories in every stories file', () => {
    expect(Object.keys(modules).length).toBeGreaterThan(0);
    for (const [path, module] of Object.entries(modules)) {
      expect(Object.keys(composeStories(module)), path).not.toHaveLength(0);
    }
  });

  for (const locale of SUPPORTED_LANGUAGES) {
    for (const theme of THEMES) {
      describe(`${locale}, ${theme} theme`, () => {
        for (const [path, module] of Object.entries(modules)) {
          const stories = composeStories(module, {
            initialGlobals: { locale, theme },
          }) as Record<string, ComposedStory>;

          for (const [name, Story] of Object.entries(stories)) {
            it(`${path} › ${name} renders translated, without axe violations`, async () => {
              // Render errors are logged, not thrown: any logged error fails the story.
              const consoleError = vi.spyOn(console, 'error');

              await Story.load();
              const { container } = render(<Story />);
              await Story.play?.({ canvasElement: container });

              const canvas = container.querySelector('[data-story-canvas]');
              expect(canvas?.childElementCount, 'story rendered nothing').toBeGreaterThan(0);
              if (Story.args.open === true) {
                expect(screen.getByRole('alertdialog')).toBeInTheDocument();
              }
              expect(document.documentElement.lang).toBe(locale);
              expect(document.documentElement.classList.contains('dark')).toBe(theme === 'dark');
              expect(untranslatedTexts(document.body)).toEqual([]);
              expect(consoleError).not.toHaveBeenCalled();
              // Portalled content (dialogs) lives outside the canvas: scan the whole document.
              expect(await axeViolations(document.body)).toEqual([]);
            });
          }
        }
      });
    }
  }
});
