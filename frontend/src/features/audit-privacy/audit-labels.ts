import type { TFunction } from 'i18next';
import type { AuditEntryResponse } from '@/api/generated/model';

/** The pages of the records an audit entry can link to (spec: Audit log screens). */
const RECORD_PAGES: Readonly<Record<string, string>> = {
  Arquebusier: '/arquebusiers',
  User: '/users',
  Comparsa: '/comparsas',
  WeaponModel: '/weapon-models',
  FestivalEdition: '/editions',
  ComparsaOrder: '/orders',
};

/** The page of the entry's record, while it still exists and has one. */
export function recordHref(entry: AuditEntryResponse): string | undefined {
  const page = RECORD_PAGES[entry.entityType];
  return page && entry.entityId && entry.recordExists ? `${page}/${entry.entityId}` : undefined;
}

export type ActorKind =
  { kind: 'user'; name: string } | { kind: 'erased' } | { kind: 'system' } | { kind: 'anonymous' };

/**
 * Who acted. Without a user, an entry recorded during a request (it has a trace id) came from
 * someone not signed in, e.g. a failed sign-in; one without a request came from the system itself,
 * e.g. the retention purge.
 */
export function actorKind(entry: AuditEntryResponse): ActorKind {
  if (entry.actor) {
    return entry.actor.erased || !entry.actor.name
      ? { kind: 'erased' }
      : { kind: 'user', name: entry.actor.name };
  }
  return entry.traceId ? { kind: 'anonymous' } : { kind: 'system' };
}

export function actorLabel(t: TFunction<'audit'>, entry: AuditEntryResponse): string {
  const actor = actorKind(entry);
  return actor.kind === 'user' ? actor.name : t(`actor.${actor.kind}`);
}

/** The action's label, or its code when the catalogue no longer declares it (a renamed code). */
export function actionLabel(t: TFunction<'audit'>, code: string): string {
  return t(`actions.${code}` as 'actions.SignedIn', { defaultValue: code });
}

/** The entity type's label, or the type itself when it is not in the catalogue. */
export function entityTypeLabel(t: TFunction<'audit'>, type: string): string {
  return t(`entityTypes.${type}` as 'entityTypes.User', { defaultValue: type });
}
