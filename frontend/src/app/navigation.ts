import type { ParseKeys } from 'i18next';
import {
  CalendarDays,
  ChartColumn,
  ClipboardList,
  Crosshair,
  Flag,
  House,
  IdCard,
  Truck,
  Users,
  type LucideIcon,
} from 'lucide-react';
import type { UserRole } from '@/api/generated/model';

export interface NavigationEntry {
  to: string;
  labelKey: ParseKeys;
  icon: LucideIcon;
  /** Roles that see the entry; every signed-in user when omitted. */
  roles?: readonly UserRole[];
  /** A counter the shell shows on the entry: `warnings`, the arquebusiers with compliance warnings. */
  count?: 'warnings';
}

/**
 * Primary navigation, as data (design D5). Feature changes add their entry here; the layout
 * needs no changes. Entries are filtered by role; the server enforces access regardless.
 */
export const NAVIGATION: readonly NavigationEntry[] = [
  { to: '/', labelKey: 'nav.home', icon: House },
  { to: '/arquebusiers', labelKey: 'nav.arquebusiers', icon: IdCard, count: 'warnings' },
  { to: '/editions', labelKey: 'nav.editions', icon: CalendarDays },
  { to: '/orders', labelKey: 'nav.orders', icon: ClipboardList },
  { to: '/distribution', labelKey: 'nav.distribution', icon: Truck },
  { to: '/statistics', labelKey: 'nav.statistics', icon: ChartColumn },
  { to: '/comparsas', labelKey: 'nav.comparsas', icon: Flag },
  { to: '/weapon-models', labelKey: 'nav.weaponModels', icon: Crosshair, roles: ['ADMIN'] },
  { to: '/users', labelKey: 'nav.users', icon: Users, roles: ['ADMIN'] },
];
