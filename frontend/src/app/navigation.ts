import type { ParseKeys } from 'i18next';
import { House, type LucideIcon } from 'lucide-react';

export interface NavigationEntry {
  to: string;
  labelKey: ParseKeys;
  icon: LucideIcon;
}

/**
 * Primary navigation, as data (design D5). Feature changes add their entry here; the layout
 * needs no changes.
 */
export const NAVIGATION: readonly NavigationEntry[] = [{ to: '/', labelKey: 'nav.home', icon: House }];
