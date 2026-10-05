import { describe, expect, it } from 'vitest';
import { currentNavigationTarget } from '@/components/app/navigation-match';
import { NAVIGATION, navigationSections, type NavigationEntry } from './navigation';

// Spec: Application shell, grouped navigation (refine-navigation-and-lists D4).

const visibleTo = (role: 'ADMIN' | 'FIRING_CHIEF') =>
  NAVIGATION.filter((entry) => !entry.roles || entry.roles.includes(role));

const shape = (entries: readonly NavigationEntry[]) =>
  navigationSections(entries).map(({ section, entries: items }) => [section, items.map((entry) => entry.to)]);

describe('navigation sections', () => {
  it('groups the entries in the specified order for an Admin', () => {
    expect(shape(visibleTo('ADMIN'))).toEqual([
      ['home', ['/']],
      ['registry', ['/arquebusiers', '/comparsas', '/statistics']],
      ['festival', ['/editions', '/orders', '/distribution']],
      ['administration', ['/weapon-models', '/users', '/audit-log', '/privacy']],
    ]);
  });

  it('leaves out a section that the role filter empties, for a FiringChief', () => {
    expect(shape(visibleTo('FIRING_CHIEF')).map(([section]) => section)).toEqual([
      'home',
      'registry',
      'festival',
    ]);
  });

  it.each([
    ['/editions/7/orders', '/orders'],
    ['/editions/7/exports', '/orders'],
    ['/editions/7/distribution', '/distribution'],
    ['/editions/7', '/editions'],
    ['/editions/7/edit', '/editions'],
  ])('marks the entry that owns %s', (pathname, expected) => {
    expect(currentNavigationTarget(pathname, NAVIGATION)).toBe(expected);
  });
});
