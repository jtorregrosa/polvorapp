/** The most badges in one PDF (spec: Badge batches). */
export const MAX_BADGES = 200;

/** The labels' languages of a badge sheet (spec: Badge language). */
export const BADGE_LANGUAGES = ['es-ES', 'ca-ES-valencia', 'en'] as const;
export type BadgeLanguage = (typeof BADGE_LANGUAGES)[number];

/** A whole comparsa, or the arquebusiers selected in the registry list. */
export type BadgeBatch =
  | { kind: 'comparsa'; comparsaId: string; comparsaName: string }
  | { kind: 'selection'; arquebusierIds: readonly string[] };
