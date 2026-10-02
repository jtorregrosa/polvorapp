/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

// Spec: Motion (design D5). Durations and easings come from the tokens and only opacity and
// transforms move. Primitives are exempt from ESLint, so these rules are checked on their source.

const ui = (file: string) => readFileSync(join(import.meta.dirname, '../components/ui', file), 'utf8');
const globals = readFileSync(join(import.meta.dirname, 'globals.css'), 'utf8');

function literals(source: string): string[] {
  return [...source.matchAll(/(['"`])((?:(?!\1)[^\\\n]|\\.)*)\1/g)].map((match) => match[2] ?? '');
}

/** The class literal of `source` that contains `marker` (single-line class lists). */
function classList(source: string, marker: string): string[] {
  const literal = literals(source).find((text) => text.includes(marker));
  if (literal === undefined) throw new Error(`No class list contains "${marker}"`);
  return literal.split(/\s+/);
}

/** Every class of every literal of `source`: negative checks must not miss a moved class. */
function allClasses(source: string): string {
  return literals(source).join(' ');
}

/** The CSS block that starts with `opening`, up to its balanced closing brace. */
function cssBlock(css: string, opening: string): string {
  const start = css.indexOf(opening);
  if (start < 0) throw new Error(`No "${opening}" block`);
  let depth = 0;
  for (let index = css.indexOf('{', start); index < css.length; index += 1) {
    if (css[index] === '{') depth += 1;
    if (css[index] === '}') depth -= 1;
    if (depth === 0) return css.slice(start, index + 1);
  }
  throw new Error(`Unbalanced "${opening}" block`);
}

describe('motion of the primitives', () => {
  it('opens side panels and the drawer in 250 ms with the drawer easing and closes them in 200 ms', () => {
    const content = classList(ui('sheet.tsx'), 'fixed z-50 flex flex-col');

    expect(content).toEqual(
      expect.arrayContaining([
        'ease-drawer',
        'data-[state=open]:duration-250',
        'data-[state=closed]:duration-200',
      ]),
    );
    expect(allClasses(ui('sheet.tsx'))).not.toMatch(/duration-(300|500)/);
  });

  it.each(['dialog.tsx', 'alert-dialog.tsx'])(
    'opens %s in 200 ms with a fade and a slight scale, and closes it in 150 ms with a fade',
    (file) => {
      const content = classList(ui(file), 'translate-x-[-50%]');

      expect(content).toEqual(
        expect.arrayContaining([
          'ease-out',
          'data-[state=open]:duration-200',
          'data-[state=closed]:duration-150',
          'data-[state=open]:zoom-in-96',
          'data-[state=closed]:fade-out-0',
        ]),
      );
      expect(allClasses(ui(file))).not.toMatch(/zoom-(in|out)-95/);
    },
  );

  it.each([
    ['dropdown-menu.tsx', 'origin-(--radix-dropdown-menu-content-transform-origin) overflow-x-hidden'],
    ['select.tsx', 'origin-(--radix-select-content-transform-origin)'],
    ['tooltip.tsx', 'origin-(--radix-tooltip-content-transform-origin)'],
  ])('shows %s in 150 ms with a fade and a 4 px slide, without zooming', (file, marker) => {
    const content = classList(ui(file), marker);

    expect(content).toEqual(expect.arrayContaining(['duration-150', 'ease-out']));
    expect(content.join(' ')).toContain('slide-in-from-top-1');
    expect(allClasses(ui(file))).not.toMatch(/zoom-(in|out)-95|slide-in-from-\w+-2\b/);
  });

  it('waits 500 ms before showing a tooltip, also in the sidebar', () => {
    expect(ui('tooltip.tsx')).toContain('delayDuration = 500');
    expect(ui('sidebar.tsx')).not.toMatch(/delayDuration=\{\d+\}/);
  });

  it('shows skeletons only after 150 ms, without a looping pulse', () => {
    const skeleton = classList(ui('skeleton.tsx'), 'rounded-md');

    expect(skeleton).toEqual(expect.arrayContaining(['delay-150', 'fill-mode-backwards', 'fade-in-0']));
    expect(skeleton).not.toContain('animate-pulse');
  });

  it('never animates the size or position of the sidebar', () => {
    expect(ui('sidebar.tsx')).not.toMatch(/transition-\[[^\]]*(width|height|left|right|top|padding)[^\]]*\]/);
    expect(ui('sidebar.tsx')).not.toContain('transition-all');
  });

  it('changes table rows at once on hover, without a transition', () => {
    expect(classList(ui('table.tsx'), 'data-[state=selected]:bg-muted')).not.toContain('transition-colors');
  });

  it('animates only colour, shadow and the press of buttons', () => {
    expect(ui('button.tsx')).not.toContain('transition-all');
  });
});

describe('reduced motion', () => {
  const block = cssBlock(globals, '@media (prefers-reduced-motion: reduce)');

  it('removes movement and scaling from enter and exit animations', () => {
    for (const declaration of [
      '--tw-enter-translate-x: 0 !important;',
      '--tw-enter-translate-y: 0 !important;',
      '--tw-enter-scale: 1 !important;',
      '--tw-exit-translate-x: 0 !important;',
      '--tw-exit-translate-y: 0 !important;',
      '--tw-exit-scale: 1 !important;',
      'scale: none !important;',
    ]) {
      expect(block).toContain(declaration);
    }
  });

  it('caps every remaining fade and transition at the fast duration, and plays them once', () => {
    expect(block).toContain('animation-duration: var(--duration-fast) !important;');
    expect(block).toContain('transition-duration: var(--duration-fast) !important;');
    expect(block).toContain('animation-iteration-count: 1 !important;');
  });

  it('stops looping animations', () => {
    expect(block).toMatch(/\.animate-pulse,\s*\.animate-spin\s*\{\s*animation: none !important;/);
  });

  it('reaches the styled select list and its icon, which `*` does not match', () => {
    expect(block).toMatch(/::picker\(select\)\s*\{[^}]*translate: none !important;/);
    expect(block).toMatch(/::picker-icon\s*\{[^}]*rotate: none !important;/);
  });
});
