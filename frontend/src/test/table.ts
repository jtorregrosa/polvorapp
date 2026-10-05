import { within } from '@testing-library/react';

/** The column titles of `table` that can be sorted (their header is a sort button). */
export function sortableColumns(table: HTMLElement): string[] {
  const head = table.querySelector('thead');
  if (!head) throw new Error('The table has no header');
  return within(head as HTMLElement)
    .queryAllByRole('button')
    .map((button) => button.firstChild?.textContent ?? '');
}
