// @ts-check
import js from '@eslint/js';
import prettier from 'eslint-config-prettier';
import betterTailwind from 'eslint-plugin-better-tailwindcss';
import i18next from 'eslint-plugin-i18next';
import jsxA11y from 'eslint-plugin-jsx-a11y';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import globals from 'globals';
import tseslint from 'typescript-eslint';

// Building blocks of the design-token guardrails (spec: No values outside the tokens).
// Each pattern matches one whole class, including variants (`hover:`, `data-[state=open]:`) and
// Tailwind's important marker (`!` before or after).
/** Any variant prefix, then an optional leading `!` and negative sign. */
const PREFIX = '(?:.*:)?!?-?';
/** Optional trailing important marker. */
const IMPORTANT = '!?';
/** Utilities that take a colour (longest first so `ring-offset` wins over `ring`). */
const COLOUR_UTILITIES = [
  'ring-offset',
  'inset-shadow',
  'inset-ring',
  'drop-shadow',
  'decoration',
  'placeholder',
  'outline',
  'border',
  'divide',
  'stroke',
  'shadow',
  'accent',
  'caret',
  'fill',
  'from',
  'text',
  'ring',
  'via',
  'bg',
  'to',
].join('|');
/** Optional side or axis of the utility (`border-t-`, `border-x-`…). */
const SIDE = '(?:-[xytrblse])?';
/** Tailwind's built-in palettes: never used directly, only through semantic tokens. */
const PALETTES = [
  'slate',
  'gray',
  'zinc',
  'neutral',
  'stone',
  'red',
  'orange',
  'amber',
  'yellow',
  'lime',
  'green',
  'emerald',
  'teal',
  'cyan',
  'sky',
  'blue',
  'indigo',
  'violet',
  'purple',
  'fuchsia',
  'pink',
  'rose',
].join('|');
/** Tokens whose contrast is verified (and their -soft/-foreground variants): no opacity on them. */
const CONTRAST_TOKENS = [
  'ring',
  'primary',
  'destructive',
  'success',
  'warning',
  'info',
  'foreground',
  'background',
].join('|');
/** Opacity modifier: `/50` or `/[.5]`. */
const OPACITY = String.raw`\/(?:\d+|\[[^\]]+\])`;

const ARBITRARY_VALUE = `^${PREFIX}[a-z0-9/.-]*\\[[^\\]]*\\]${IMPORTANT}$`;
const RAW_PALETTE = `^${PREFIX}(?:${COLOUR_UTILITIES})${SIDE}-(?:(?:${PALETTES})-\\d{2,3}|black|white)(?:${OPACITY})?${IMPORTANT}$`;
const TOKEN_OPACITY = `^${PREFIX}(?:${COLOUR_UTILITIES})${SIDE}-(?:${CONTRAST_TOKENS})(?:-[a-z]+)*${OPACITY}${IMPORTANT}$`;

export default tseslint.config(
  {
    ignores: [
      'dist',
      'dev-dist',
      'coverage',
      'playwright-report',
      'test-results',
      'storybook-static',
      'src/api/generated',
    ],
  },
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      ...tseslint.configs.strictTypeChecked,
      ...tseslint.configs.stylisticTypeChecked,
    ],
    languageOptions: {
      ecmaVersion: 2023,
      globals: globals.browser,
      parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname },
    },
    rules: {
      '@typescript-eslint/restrict-template-expressions': ['error', { allowNumber: true }],
    },
  },
  {
    files: ['src/**/*.{ts,tsx}'],
    extends: [
      reactHooks.configs.flat['recommended-latest'],
      jsxA11y.flatConfigs.strict,
      reactRefresh.configs.vite,
    ],
  },
  {
    // Tailwind correctness everywhere: unknown, duplicate, conflicting or deprecated classes.
    files: ['src/**/*.{ts,tsx}'],
    extends: [betterTailwind.configs['correctness-error']],
    settings: { 'better-tailwindcss': { entryPoint: 'src/styles/globals.css' } },
  },
  {
    // Closed design tokens (ADR-0009 §1, spec: No values outside the tokens). Primitives in
    // components/ui stay close to upstream and are exempt.
    files: ['src/**/*.{ts,tsx}'],
    ignores: ['src/components/ui/**'],
    rules: {
      'better-tailwindcss/no-restricted-classes': [
        'error',
        {
          restrict: [
            {
              pattern: ARBITRARY_VALUE,
              message: 'Arbitrary values are not allowed: use a design token (docs/design/tokens.md).',
            },
            {
              pattern: RAW_PALETTE,
              message:
                'Raw palette colours are not allowed: use a semantic token (primary, success, muted…).',
            },
            {
              pattern: TOKEN_OPACITY,
              message: 'Opacity on semantic tones or the focus ring breaks the verified contrast.',
            },
          ],
        },
      ],
    },
  },
  {
    // Feature screens and the app shell use composites only (ADR-0009 §2).
    files: ['src/features/**/*.{ts,tsx}', 'src/app/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['@/components/ui', '@/components/ui/*', '**/components/ui/*'],
              message: 'Use a composite from @/components/app instead of a primitive (ADR-0009).',
            },
          ],
        },
      ],
    },
  },
  {
    files: ['src/features/**/*.tsx', 'src/components/app/**/*.tsx', 'src/app/**/*.tsx'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector: "JSXAttribute[name.name='style']",
          message: 'Inline styles are not allowed: use design-token classes.',
        },
      ],
    },
  },
  {
    // Every user-facing text comes from translations (NFR-02, ADR-0007).
    files: ['src/**/*.tsx'],
    ignores: ['src/**/*.test.tsx', 'src/test/**'],
    extends: [i18next.configs['flat/recommended']],
    rules: {
      'i18next/no-literal-string': [
        'error',
        { mode: 'jsx-only', 'jsx-attributes': { include: ['alt', 'aria-label', 'title', 'placeholder'] } },
      ],
    },
  },
  {
    // Catalogue stories render synthetic sample content (not UI copy) and export story objects.
    files: ['src/**/*.stories.tsx', '.storybook/**/*.{ts,tsx}'],
    rules: {
      'i18next/no-literal-string': 'off',
      'react-refresh/only-export-components': 'off',
    },
  },
  {
    // shadcn/ui primitives stay close to upstream (ADR-0009): only style/strictness rules are relaxed;
    // i18n, hooks-of-rules and type-safety rules still apply. Local edits: translated texts.
    files: ['src/components/ui/**/*.tsx', 'src/hooks/use-mobile.ts'],
    rules: {
      'react-refresh/only-export-components': 'off',
      '@typescript-eslint/consistent-type-definitions': 'off',
      '@typescript-eslint/no-confusing-void-expression': 'off',
      '@typescript-eslint/no-unnecessary-condition': 'off',
      '@typescript-eslint/no-unnecessary-template-expression': 'off',
      '@typescript-eslint/no-unnecessary-type-conversion': 'off',
    },
  },
  {
    // Upstream sidebar skeleton randomises its width during render.
    files: ['src/components/ui/sidebar.tsx'],
    rules: { 'react-hooks/purity': 'off' },
  },
  {
    // Upstream media-query hook syncs state from matchMedia in an effect.
    files: ['src/hooks/use-mobile.ts'],
    rules: { 'react-hooks/set-state-in-effect': 'off' },
  },
  {
    files: ['*.config.{js,ts}', 'scripts/**/*.mjs', 'e2e/**/*.ts'],
    languageOptions: { globals: globals.node },
  },
  {
    files: ['**/*.{js,mjs}'],
    extends: [js.configs.recommended],
    languageOptions: { globals: globals.node },
  },
  {
    // Static scripts served as-is to the browser (e.g. theme-init.js).
    files: ['public/**/*.js'],
    languageOptions: { globals: globals.browser, sourceType: 'script' },
    rules: { 'no-unused-vars': ['error', { caughtErrors: 'none' }] },
  },
  prettier,
);
