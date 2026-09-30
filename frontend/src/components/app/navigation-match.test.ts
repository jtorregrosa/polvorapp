import { describe, expect, it } from 'vitest';
import { isCurrentPath } from './navigation-match';

describe('isCurrentPath', () => {
  it.each([
    ['/', '/', true],
    ['/orders', '/', false],
    ['/orders', '/orders', true],
    ['/orders/2027', '/orders', true],
    ['/orders-archive', '/orders', false],
    ['/order', '/orders', false],
  ])('%s is current for the entry %s: %s', (pathname, to, expected) => {
    expect(isCurrentPath(pathname, to)).toBe(expected);
  });
});
