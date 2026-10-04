import { describe, expect, it } from 'vitest';
import type { AuditEntryResponse } from '@/api/generated/model';
import { actorKind, recordHref } from './audit-labels';

const ENTRY: AuditEntryResponse = {
  id: '00000000-0000-4000-8000-000000000a01',
  occurredAt: '2030-07-12T16:05:00Z',
  action: 'ArquebusierUpdated',
  entityType: 'Arquebusier',
  entityId: '00000000-0000-4000-8000-000000000301',
  recordExists: true,
  comparsaId: null,
  comparsaName: null,
  traceId: '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01',
  actor: { id: '00000000-0000-4000-8000-000000000001', name: 'Administradora Sintética', erased: false },
  data: null,
};

describe('recordHref', () => {
  it.each([
    ['Arquebusier', '/arquebusiers/'],
    ['User', '/users/'],
    ['Comparsa', '/comparsas/'],
    ['WeaponModel', '/weapon-models/'],
    ['FestivalEdition', '/editions/'],
    ['ComparsaOrder', '/orders/'],
  ])('links an existing %s to its page', (entityType, path) => {
    expect(recordHref({ ...ENTRY, entityType })).toBe(`${path}${ENTRY.entityId}`);
  });

  it('does not link a record that no longer exists', () => {
    expect(recordHref({ ...ENTRY, recordExists: false })).toBeUndefined();
  });

  it.each(['EditionEntry', 'PickupProxy', 'AuditTrail', 'Retired'])(
    'does not link a %s, which has no page',
    (entityType) => {
      expect(recordHref({ ...ENTRY, entityType })).toBeUndefined();
    },
  );

  it('does not link an entry without a record', () => {
    expect(recordHref({ ...ENTRY, entityId: null })).toBeUndefined();
  });
});

describe('actorKind', () => {
  it('names a user, an erased user, the system and an anonymous request', () => {
    expect(actorKind(ENTRY)).toEqual({ kind: 'user', name: 'Administradora Sintética' });
    expect(
      actorKind({
        ...ENTRY,
        actor: { id: '00000000-0000-4000-8000-000000000002', name: null, erased: true },
      }),
    ).toEqual({ kind: 'erased' });
    expect(actorKind({ ...ENTRY, actor: null, traceId: null })).toEqual({ kind: 'system' });
    expect(actorKind({ ...ENTRY, actor: null })).toEqual({ kind: 'anonymous' });
  });
});
