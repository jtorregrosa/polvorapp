/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { checkContrast, contrastRatio, describeFailure, parseThemes, type ContrastPair } from './contrast';

const tokensCss = readFileSync(join(import.meta.dirname, 'tokens.css'), 'utf8');

const TEXT = 4.5; // WCAG 2.2 AA, normal text (1.4.3)
const UI = 3; // WCAG 2.2 AA, non-text contrast (1.4.11)

/** Token pairs that are used together in the UI (design D2, D3; ADR-0013). */
const PAIRS: ContrastPair[] = [
  { foreground: 'foreground', background: 'background', minimum: TEXT },
  ...(['muted', 'secondary', 'accent', 'surface-2'] as const).map((surface) => ({
    foreground: 'foreground',
    background: surface,
    minimum: TEXT,
  })),
  { foreground: 'card-foreground', background: 'card', minimum: TEXT },
  { foreground: 'popover-foreground', background: 'popover', minimum: TEXT },
  { foreground: 'muted-foreground', background: 'background', minimum: TEXT },
  { foreground: 'muted-foreground', background: 'card', minimum: TEXT },
  { foreground: 'muted-foreground', background: 'muted', minimum: TEXT },
  { foreground: 'muted-foreground', background: 'secondary', minimum: TEXT },
  { foreground: 'muted-foreground', background: 'accent', minimum: TEXT },
  { foreground: 'muted-foreground', background: 'surface-2', minimum: TEXT },
  { foreground: 'muted-foreground', background: 'popover', minimum: TEXT },
  { foreground: 'secondary-foreground', background: 'secondary', minimum: TEXT },
  { foreground: 'accent-foreground', background: 'accent', minimum: TEXT },
  { foreground: 'primary-foreground', background: 'primary', minimum: TEXT },
  { foreground: 'primary', background: 'card', minimum: TEXT },
  { foreground: 'primary', background: 'background', minimum: TEXT },
  { foreground: 'primary-soft-foreground', background: 'primary-soft', minimum: TEXT },
  { foreground: 'primary-soft-foreground', background: 'card', minimum: TEXT },
  { foreground: 'primary', background: 'primary-soft', minimum: TEXT },
  { foreground: 'primary', background: 'muted', minimum: TEXT },
  { foreground: 'primary', background: 'secondary', minimum: TEXT },
  { foreground: 'primary', background: 'surface-2', minimum: TEXT },
  { foreground: 'primary', background: 'accent', minimum: TEXT },
  { foreground: 'primary', background: 'secondary-hover', minimum: TEXT },
  // Hover and pressed states of filled buttons are solid tokens, never an opacity (design D3).
  { foreground: 'primary-foreground', background: 'primary-hover', minimum: TEXT },
  { foreground: 'destructive-foreground', background: 'destructive-hover', minimum: TEXT },
  { foreground: 'secondary-foreground', background: 'secondary-hover', minimum: TEXT },
  { foreground: 'muted-foreground', background: 'secondary-hover', minimum: TEXT },
  ...(['success', 'warning', 'destructive', 'info'] as const).flatMap((tone) => [
    { foreground: `${tone}-foreground`, background: tone, minimum: TEXT },
    { foreground: `${tone}-soft-foreground`, background: `${tone}-soft`, minimum: TEXT },
    { foreground: tone, background: 'card', minimum: TEXT },
    { foreground: tone, background: 'background', minimum: TEXT },
    { foreground: `${tone}-soft-foreground`, background: 'card', minimum: TEXT },
    { foreground: `${tone}-soft-foreground`, background: 'background', minimum: TEXT },
  ]),
  // Tags for fixed values: categorical tones, never semantic ones (refine-navigation-and-lists D5).
  ...(['tag-1', 'tag-2', 'tag-3', 'tag-4', 'tag-neutral'] as const).map((tone) => ({
    foreground: `${tone}-foreground`,
    background: tone,
    minimum: TEXT,
  })),
  { foreground: 'destructive', background: 'muted', minimum: TEXT },
  { foreground: 'destructive', background: 'surface-2', minimum: TEXT },
  // The night sidebar is dark in both themes (ADR-0013): its own text, accent and focus pairs.
  { foreground: 'sidebar-foreground', background: 'sidebar', minimum: TEXT },
  { foreground: 'sidebar-muted-foreground', background: 'sidebar', minimum: TEXT },
  { foreground: 'sidebar-accent-foreground', background: 'sidebar-accent', minimum: TEXT },
  { foreground: 'sidebar-foreground', background: 'sidebar-accent', minimum: TEXT },
  { foreground: 'sidebar-muted-foreground', background: 'sidebar-accent', minimum: TEXT },
  { foreground: 'sidebar-primary', background: 'sidebar', minimum: TEXT },
  { foreground: 'sidebar-primary', background: 'sidebar-accent', minimum: TEXT },
  { foreground: 'sidebar-primary-foreground', background: 'sidebar-primary', minimum: TEXT },
  { foreground: 'input', background: 'card', minimum: UI },
  { foreground: 'input', background: 'background', minimum: UI },
  { foreground: 'ring', background: 'background', minimum: UI },
  { foreground: 'ring', background: 'card', minimum: UI },
  { foreground: 'input', background: 'popover', minimum: UI },
  { foreground: 'input', background: 'muted', minimum: UI },
  { foreground: 'input', background: 'secondary', minimum: UI },
  { foreground: 'ring', background: 'popover', minimum: UI },
  { foreground: 'ring', background: 'surface-2', minimum: UI },
  { foreground: 'ring', background: 'accent', minimum: UI },
  { foreground: 'input', background: 'surface-2', minimum: UI },
  { foreground: 'sidebar-ring', background: 'sidebar', minimum: UI },
  { foreground: 'sidebar-ring', background: 'sidebar-accent', minimum: UI },
  // Comparsa logos: the placeholder icon on the light tile, in both themes, and the sidebar cards'
  // name at rest and on hover (add-comparsa-logos D8).
  { foreground: 'logo-tile-foreground', background: 'logo-tile', minimum: UI },
  { foreground: 'sidebar-foreground', background: 'sidebar-border', minimum: TEXT },
  { foreground: 'sidebar-ring', background: 'sidebar-border', minimum: UI },
];

describe('design token contrast (WCAG 2.2 AA)', () => {
  const themes = parseThemes(tokensCss);

  it('defines a light and a dark theme with the same tokens', () => {
    expect(Object.keys(themes)).toEqual(['light', 'dark']);
    expect(Object.keys(themes.dark).sort()).toEqual(Object.keys(themes.light).sort());
  });

  it.each(['light', 'dark'] as const)('meets every declared pair in the %s theme', (theme) => {
    const failures = checkContrast(themes[theme], PAIRS).map((failure) => describeFailure(theme, failure));

    expect(failures).toEqual([]);
  });
});

describe('contrast helpers', () => {
  it('computes WCAG ratios', () => {
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 1);
    expect(contrastRatio('#777777', '#ffffff')).toBeCloseTo(4.48, 2);
  });

  it('reports a failing pair with its ratio', () => {
    const failures = checkContrast({ text: '#999999', surface: '#ffffff' }, [
      { foreground: 'text', background: 'surface', minimum: TEXT },
    ]);

    expect(failures).toHaveLength(1);
    expect(failures[0]?.ratio).toBeCloseTo(2.85, 2);
    expect(failures.map((failure) => describeFailure('dark', failure))).toEqual([
      'dark: text on surface = 2.85 (< 4.5)',
    ]);
  });

  it('gives the installed app the theme colours of the tokens', () => {
    const viteConfig = readFileSync(join(import.meta.dirname, '../../vite.config.ts'), 'utf8');
    const { light } = parseThemes(tokensCss);

    expect(viteConfig).toContain(`theme_color: '${light.primary ?? ''}'`);
    expect(viteConfig).toContain(`background_color: '${light.background ?? ''}'`);
  });

  it('rejects colour tokens it cannot evaluate instead of skipping them', () => {
    expect(() => parseThemes(':root {\n  --primary: oklch(0.5 0.1 40);\n}\n.dark {\n}\n')).toThrow(/primary/);
  });

  it('reports a pair that references an undefined token', () => {
    expect(() =>
      checkContrast({ text: '#000000' }, [{ foreground: 'text', background: 'missing', minimum: TEXT }]),
    ).toThrow(/missing/);
  });
});
