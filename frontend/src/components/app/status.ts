import {
  CircleAlert,
  CircleCheck,
  CircleDashed,
  CircleX,
  Clock,
  Lock,
  Send,
  TriangleAlert,
  Undo2,
  type LucideIcon,
} from 'lucide-react';

/** Semantic tone of a status (design D1/D6); each maps to verified token pairs. */
export type StatusTone = 'success' | 'warning' | 'destructive' | 'info' | 'muted';

export interface StatusStyle {
  tone: StatusTone;
  icon: LucideIcon;
}

/**
 * The single mapping from domain statuses (glossary code terms) to tone and icon
 * (spec: Status semantics). Labels live in the `ui` namespace under `status.<kind>.<VALUE>`.
 * Compliance warnings (BR-04) are warnings, never errors.
 */
export const STATUS_MAP = {
  license: {
    VALID: { tone: 'success', icon: CircleCheck },
    EXPIRING: { tone: 'warning', icon: TriangleAlert },
    EXPIRED: { tone: 'destructive', icon: CircleX },
    PENDING: { tone: 'info', icon: Clock },
  },
  arquebusier: {
    ACTIVE: { tone: 'success', icon: CircleCheck },
    RESERVE: { tone: 'muted', icon: CircleDashed },
  },
  order: {
    DRAFT: { tone: 'muted', icon: CircleDashed },
    SUBMITTED: { tone: 'info', icon: Send },
    RETURNED: { tone: 'warning', icon: Undo2 },
    VALIDATED: { tone: 'success', icon: CircleCheck },
  },
  edition: {
    DRAFT: { tone: 'muted', icon: CircleDashed },
    ORDERS_OPEN: { tone: 'success', icon: CircleCheck },
    CORRECTIONS_OPEN: { tone: 'warning', icon: TriangleAlert },
    LOCKED: { tone: 'info', icon: Lock },
    CLOSED: { tone: 'muted', icon: CircleDashed },
  },
  user: {
    INVITED: { tone: 'info', icon: Send },
    ACTIVE: { tone: 'success', icon: CircleCheck },
    DEACTIVATED: { tone: 'muted', icon: CircleX },
  },
  /** Comparsas and weapon models (federation-catalog): deactivated records keep their history. */
  catalog: {
    ACTIVE: { tone: 'success', icon: CircleCheck },
    INACTIVE: { tone: 'muted', icon: CircleDashed },
  },
  warning: {
    LICENSE: { tone: 'warning', icon: TriangleAlert },
    COURSE: { tone: 'warning', icon: TriangleAlert },
    AGE: { tone: 'warning', icon: TriangleAlert },
  },
} as const satisfies Record<string, Record<string, StatusStyle>>;

export type StatusKind = keyof typeof STATUS_MAP;

/** Fallback for values outside the mapping: neutral, with the raw code as label. */
export const UNKNOWN_STATUS: StatusStyle = { tone: 'muted', icon: CircleAlert };

export function statusStyle(kind: StatusKind, value: string): StatusStyle | undefined {
  return (STATUS_MAP[kind] as Record<string, StatusStyle | undefined>)[value];
}
