import { describe, expect, it } from 'vitest';
import { currentNavigationTarget, isCurrentPath } from './navigation-match';

describe('isCurrentPath', () => {
  it.each([
    ['/', '/', true],
    ['/orders', '/', false],
    ['/orders', '/orders', true],
    ['/orders/2027', '/orders', true],
    ['/orders-archive', '/orders', false],
    ['/order', '/orders', false],
    ['/editions/7/orders', '/editions/*/orders', true],
    ['/editions/7/orders/3', '/editions/*/orders', true],
    ['/editions/7', '/editions/*/orders', false],
  ])('%s is current for the entry %s: %s', (pathname, to, expected) => {
    expect(isCurrentPath(pathname, to)).toBe(expected);
  });
});

describe('currentNavigationTarget', () => {
  const entries = [
    { to: '/' },
    { to: '/editions' },
    { to: '/orders', matches: ['/editions/*/orders', '/editions/*/exports'] },
    { to: '/distribution', matches: ['/editions/*/distribution'] },
  ];

  it.each([
    ['/', '/'],
    ['/editions', '/editions'],
    ['/editions/7', '/editions'],
    ['/editions/7/orders', '/orders'],
    ['/editions/7/exports', '/orders'],
    ['/editions/7/distribution', '/distribution'],
    ['/orders/3', '/orders'],
    ['/unknown', undefined],
  ])('marks the entry for %s as %s', (pathname, expected) => {
    expect(currentNavigationTarget(pathname, entries)).toBe(expected);
  });
});
