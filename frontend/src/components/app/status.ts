import {
  Archive,
  CircleAlert,
  CircleCheck,
  CircleDashed,
  CirclePlay,
  CircleX,
  Clock,
  Lock,
  Send,
  Sparkles,
  TriangleAlert,
  Undo2,
  type LucideIcon,
} from 'lucide-react';
import type { ComplianceWarning, EditionStatus } from '@/api/generated/model';

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
  /** Festival editions (`EditionStatus`, add-festival-editions): the lifecycle only. */
  edition: {
    DRAFT: { tone: 'muted', icon: CircleDashed },
    IN_PROGRESS: { tone: 'success', icon: CirclePlay },
    CLOSED: { tone: 'muted', icon: Archive },
  },
  /** An arquebusier's participation (add-comparsa-orders): the first-year flag (UC-07). */
  participation: {
    FIRST_YEAR: { tone: 'info', icon: Sparkles },
  },
  /** Whether FiringChiefs may edit the orders of the edition in progress (BR-10). */
  orders: {
    OPEN: { tone: 'success', icon: CircleCheck },
    CLOSED: { tone: 'info', icon: Lock },
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
  /** Compliance warnings (BR-04, `ComplianceWarning`), in rule order. */
  warning: {
    LICENSE_MISSING: { tone: 'warning', icon: TriangleAlert },
    LICENSE_PENDING: { tone: 'warning', icon: TriangleAlert },
    LICENSE_EXPIRED: { tone: 'warning', icon: TriangleAlert },
    LICENSE_EXPIRING: { tone: 'warning', icon: TriangleAlert },
    COURSE_MISSING: { tone: 'warning', icon: TriangleAlert },
    UNDER_AGE: { tone: 'warning', icon: TriangleAlert },
    ID_PHOTO_MISSING: { tone: 'warning', icon: TriangleAlert },
    LICENSE_PHOTOS_MISSING: { tone: 'warning', icon: TriangleAlert },
  },
} as const satisfies Record<string, Record<string, StatusStyle>> & {
  // Exactly the API's codes: a new or removed code fails the type check here.
  warning: Record<ComplianceWarning, StatusStyle>;
  edition: Record<EditionStatus, StatusStyle>;
};

export type StatusKind = keyof typeof STATUS_MAP;

/** Fallback for values outside the mapping: neutral, with the raw code as label. */
export const UNKNOWN_STATUS: StatusStyle = { tone: 'muted', icon: CircleAlert };

export function statusStyle(kind: StatusKind, value: string): StatusStyle | undefined {
  return (STATUS_MAP[kind] as Record<string, StatusStyle | undefined>)[value];
}
