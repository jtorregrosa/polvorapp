/// <reference types="node" />
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

// Spec: Accessible composites, Design tokens with light and dark themes (design D3). ESLint exempts
// this folder so primitives stay close to upstream, so this test guards the local edits that fix
// focus, state and target-size defects. Each failure names the file and the offending class.

const folder = import.meta.dirname;

/** Source without comments, so an apostrophe in a comment cannot pair with a later quote. */
function withoutComments(source: string): string {
  return source
    .replace(/\{\/\*[\s\S]*?\*\/\}/g, '')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/^\s*\/\/.*$/gm, '')
    .replace(/\s\/\/ .*$/gm, '');
}

const primitives = readdirSync(folder)
  .filter((file) => file.endsWith('.tsx'))
  .map((file) => ({ file, source: withoutComments(readFileSync(join(folder, file), 'utf8')) }));

/** Every string literal of a source file, template literals included. */
function literals(source: string): string[] {
  return [...source.matchAll(/(['"`])((?:(?!\1)[^\\]|\\.)*)\1/g)].map((match) => match[2] ?? '');
}

function classes(source: string): string[] {
  return literals(source)
    .flatMap((literal) => literal.split(/\s+/))
    .filter(Boolean);
}

/** Semantic colour tokens: an opacity modifier on them breaks the verified contrast. */
const SEMANTIC =
  '(?:sidebar-)?(?:ring|primary|destructive|input|secondary|accent|foreground|success|warning|info|muted|card|popover|border|background)';
const COLOUR_UTILITY =
  '(?:bg|text|border|ring|outline|fill|stroke|shadow|decoration|caret|divide|placeholder)';
const TRANSLUCENT_STATE = new RegExp(
  `^(?:.*:)?!?${COLOUR_UTILITY}-(?:[xytrblse]-)?${SEMANTIC}(?:-[a-z]+)*/(?:\\d+|\\[[^\\]]+\\])!?$`,
);

function offending(test: (cls: string) => boolean): string[] {
  return primitives.flatMap(({ file, source }) =>
    classes(source)
      .filter(test)
      .map((cls) => `${file}: ${cls}`),
  );
}

/** The class lists (split literals) of `file` that contain `marker`. */
function classLists(file: string, marker: string): string[][] {
  const primitive = primitives.find((entry) => entry.file === file);
  if (!primitive) throw new Error(`Primitive ${file} not found`);
  return literals(primitive.source)
    .filter((literal) => literal.includes(marker))
    .map((literal) => literal.split(/\s+/));
}

const FOCUS_OUTLINE = ['focus-visible:outline-2', 'focus-visible:outline-ring'];

describe('shadcn/ui primitives (local edits)', () => {
  it('scans every primitive and its class lists', () => {
    expect(primitives.length).toBeGreaterThanOrEqual(20);
    for (const { file, source } of primitives) expect(classes(source).length, file).toBeGreaterThan(0);
    expect(TRANSLUCENT_STATE.test('hover:bg-primary/90')).toBe(true);
    expect(TRANSLUCENT_STATE.test('placeholder:text-muted-foreground/[.7]')).toBe(true);
    expect(TRANSLUCENT_STATE.test('bg-black/50')).toBe(false);
  });

  it('use no opacity on verified tokens for focus, hover or pressed states', () => {
    expect(offending((cls) => TRANSLUCENT_STATE.test(cls))).toEqual([]);
  });

  it('use the destructive token pair instead of white text', () => {
    expect(offending((cls) => /(?:^|:)text-white$/.test(cls))).toEqual([]);
  });

  it.each([
    'button.tsx',
    'input.tsx',
    'textarea.tsx',
    'native-select.tsx',
    'checkbox.tsx',
    'radio-group.tsx',
    'tabs.tsx',
  ])('draw a full-opacity 2 px focus outline on every control of %s', (file) => {
    const lists = classLists(file, 'focus-visible:');
    expect(lists.length).toBeGreaterThan(0);
    for (const list of lists) expect(list).toEqual(expect.arrayContaining(FOCUS_OUTLINE));
  });

  it.each(['dropdown-menu.tsx', 'select.tsx'])(
    'show keyboard focus on menu items in %s with an outline, not only a background',
    (file) => {
      const items = classLists(file, 'focus:bg-accent');
      expect(items.length).toBeGreaterThan(0);
      for (const item of items) expect(item).toEqual(expect.arrayContaining(FOCUS_OUTLINE));
    },
  );

  it.each(['checkbox.tsx', 'radio-group.tsx'])('give the 20 px box of %s a larger hit area', (file) => {
    const [box] = classLists(file, 'size-5');
    expect(box).toEqual(
      expect.arrayContaining(['aspect-square', 'min-w-5', 'after:-inset-1', 'pointer-coarse:after:-inset-3']),
    );
  });

  it.each(['dialog.tsx', 'sheet.tsx'])(
    'give the close button of %s a 36 px target (44 px on touch)',
    (file) => {
      const close = classLists(file, 'absolute top-');
      expect(close.length).toBeGreaterThan(0);
      for (const item of close)
        expect(item).toEqual(expect.arrayContaining(['size-9', 'pointer-coarse:size-11']));
    },
  );
});
