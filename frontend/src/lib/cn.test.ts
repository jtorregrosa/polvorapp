/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { cn } from './cn';

const tokens = readFileSync(join(import.meta.dirname, '../styles/tokens.css'), 'utf8');

/** The names of a `@theme` namespace in tokens.css, e.g. `text` → page, record… */
function names(namespace: string): string[] {
  return [...tokens.matchAll(new RegExp(`--${namespace}-([a-z0-9-]+):`, 'g'))]
    .map((match) => match[1] ?? '')
    .filter((name) => !name.includes('--'));
}

describe('cn', () => {
  it('keeps a type role next to a text colour', () => {
    expect(cn('text-label text-foreground', 'text-primary-foreground')).toBe(
      'text-label text-primary-foreground',
    );
  });

  it('lets a later type role replace an earlier one', () => {
    expect(cn('text-body', 'text-help')).toBe('text-help');
  });

  it('merges the named spacing, widths and shadows of the tokens', () => {
    expect(cn('gap-field', 'gap-group')).toBe('gap-group');
    expect(cn('max-w-field-id', 'max-w-page')).toBe('max-w-page');
    expect(cn('shadow-e1', 'shadow-e2')).toBe('shadow-e2');
    expect(cn('h-control', 'h-11')).toBe('h-11');
  });

  it.each([
    ['text', ['page', 'record', 'section', 'figure', 'body', 'label', 'help', 'id']],
    ['shadow', ['e1', 'e2']],
  ])('knows every %s token of tokens.css', (namespace, known) => {
    expect(names(namespace).sort()).toEqual([...known].sort());
  });
});
