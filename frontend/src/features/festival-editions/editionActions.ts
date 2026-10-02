import {
  CalendarX,
  LockKeyhole,
  LockKeyholeOpen,
  Play,
  RotateCcw,
  Trash2,
  Undo2,
  type LucideIcon,
} from 'lucide-react';
import type { EditionResponse, EditionStatus } from '@/api/generated/model';

/** Every action an Admin can take on an edition, each with its confirmation and outcome texts. */
export type EditionAction =
  'start' | 'openOrders' | 'closeOrders' | 'closeEdition' | 'backToDraft' | 'reopen' | 'delete';

export const ACTION_ICONS: Readonly<Record<EditionAction, LucideIcon>> = {
  start: Play,
  openOrders: LockKeyholeOpen,
  closeOrders: LockKeyhole,
  closeEdition: CalendarX,
  backToDraft: Undo2,
  reopen: RotateCcw,
  delete: Trash2,
};

/** The status each lifecycle move leads to (spec: Edition lifecycle (UC-11)). */
export const MOVES: Readonly<Partial<Record<EditionAction, EditionStatus>>> = {
  start: 'IN_PROGRESS',
  closeEdition: 'CLOSED',
  backToDraft: 'DRAFT',
  reopen: 'IN_PROGRESS',
};

/**
 * The state's primary action and its "More actions" (design D10, spec: Editions screens): a draft
 * starts or is deleted; the edition in progress opens or closes its orders, and is closed or sent
 * back to preparation only with its orders closed; a closed edition reopens.
 */
export function actionsFor(edition: Pick<EditionResponse, 'status' | 'ordersOpen'>): {
  primary?: EditionAction;
  more: EditionAction[];
} {
  switch (edition.status) {
    case 'DRAFT':
      return { primary: 'start', more: ['delete'] };
    case 'IN_PROGRESS':
      return {
        primary: edition.ordersOpen ? 'closeOrders' : 'openOrders',
        more: ['closeEdition', 'backToDraft'],
      };
    default:
      return { more: ['reopen'] };
  }
}
