import type { ParseKeys } from 'i18next';
import {
  CalendarDays,
  ChartColumn,
  ClipboardList,
  Crosshair,
  Flag,
  House,
  IdCard,
  ScrollText,
  Settings,
  ShieldCheck,
  Truck,
  Users,
  type LucideIcon,
} from 'lucide-react';
import type { UserRole } from '@/api/generated/model';

/** The sections of the navigation, in order (refine-navigation-and-lists D4); `home` has no label. */
export const NAVIGATION_SECTIONS = ['home', 'registry', 'festival', 'administration'] as const;

export type NavigationSectionId = (typeof NAVIGATION_SECTIONS)[number];

export interface NavigationEntry {
  to: string;
  section: NavigationSectionId;
  labelKey: ParseKeys;
  icon: LucideIcon;
  /** Roles that see the entry; every signed-in user when omitted. */
  roles?: readonly UserRole[];
  /** Other paths the entry owns, `*` being any one segment: an edition's orders belong to Orders. */
  matches?: readonly string[];
  /** A counter the shell shows on the entry: `warnings`, the arquebusiers with compliance warnings. */
  count?: 'warnings';
  /** Also in a FiringChief's bottom bar on phones (UI audit): their everyday destinations. */
  bottomBar?: boolean;
}

/**
 * Primary navigation, as data (design D5). Feature changes add their entry here; the layout
 * needs no changes. Entries are filtered by role; the server enforces access regardless.
 */
export const NAVIGATION: readonly NavigationEntry[] = [
  { to: '/', section: 'home', labelKey: 'nav.home', icon: House, bottomBar: true },
  {
    to: '/arquebusiers',
    section: 'registry',
    labelKey: 'nav.arquebusiers',
    icon: IdCard,
    count: 'warnings',
    bottomBar: true,
  },
  { to: '/comparsas', section: 'registry', labelKey: 'nav.comparsas', icon: Flag },
  { to: '/statistics', section: 'registry', labelKey: 'nav.statistics', icon: ChartColumn },
  { to: '/editions', section: 'festival', labelKey: 'nav.editions', icon: CalendarDays },
  {
    to: '/orders',
    section: 'festival',
    labelKey: 'nav.orders',
    icon: ClipboardList,
    matches: ['/editions/*/orders', '/editions/*/exports'],
    bottomBar: true,
  },
  {
    to: '/distribution',
    section: 'festival',
    labelKey: 'nav.distribution',
    icon: Truck,
    matches: ['/editions/*/distribution'],
    bottomBar: true,
  },
  {
    to: '/weapon-models',
    section: 'administration',
    labelKey: 'nav.weaponModels',
    icon: Crosshair,
    roles: ['ADMIN'],
  },
  { to: '/users', section: 'administration', labelKey: 'nav.users', icon: Users, roles: ['ADMIN'] },
  {
    to: '/audit-log',
    section: 'administration',
    labelKey: 'nav.auditLog',
    icon: ScrollText,
    roles: ['ADMIN'],
  },
  { to: '/privacy', section: 'administration', labelKey: 'nav.privacy', icon: ShieldCheck, roles: ['ADMIN'] },
  { to: '/settings', section: 'administration', labelKey: 'nav.settings', icon: Settings, roles: ['ADMIN'] },
];

/** The entries a user with `role` sees (none of the role-restricted ones without a role). */
export function navigationFor(role: UserRole | undefined): readonly NavigationEntry[] {
  return NAVIGATION.filter((entry) => !entry.roles || (role !== undefined && entry.roles.includes(role)));
}

/** A FiringChief's bottom bar on phones (UI audit): Home, Arquebusiers, Orders and Distribution. */
export function bottomBarFor(role: UserRole | undefined): readonly NavigationEntry[] {
  return role === 'FIRING_CHIEF' ? navigationFor(role).filter((entry) => entry.bottomBar === true) : [];
}

/**
 * `entries` grouped by section, in the sections' order and keeping the entries' order; a section
 * left empty (e.g. by the role filter) is left out.
 */
export function navigationSections(
  entries: readonly NavigationEntry[],
): { section: NavigationSectionId; entries: readonly NavigationEntry[] }[] {
  return NAVIGATION_SECTIONS.map((section) => ({
    section,
    entries: entries.filter((entry) => entry.section === section),
  })).filter((group) => group.entries.length > 0);
}
