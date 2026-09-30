import type { ParseKeys } from 'i18next';
import { Crosshair, Flag, House, Users, type LucideIcon } from 'lucide-react';
import type { UserRole } from '@/api/generated/model';

export interface NavigationEntry {
  to: string;
  labelKey: ParseKeys;
  icon: LucideIcon;
  /** Roles that see the entry; every signed-in user when omitted. */
  roles?: readonly UserRole[];
}

/**
 * Primary navigation, as data (design D5). Feature changes add their entry here; the layout
 * needs no changes. Entries are filtered by role; the server enforces access regardless.
 */
export const NAVIGATION: readonly NavigationEntry[] = [
  { to: '/', labelKey: 'nav.home', icon: House },
  { to: '/comparsas', labelKey: 'nav.comparsas', icon: Flag },
  { to: '/weapon-models', labelKey: 'nav.weaponModels', icon: Crosshair, roles: ['ADMIN'] },
  { to: '/users', labelKey: 'nav.users', icon: Users, roles: ['ADMIN'] },
];
