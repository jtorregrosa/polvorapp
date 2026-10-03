import { QueryClient } from '@tanstack/react-query';
import { describe, expect, it } from 'vitest';
import {
  getGetComparsaOrderQueryKey,
  getGetComparsaOrdersOverviewQueryKey,
} from '@/api/generated/comparsa-orders/comparsa-orders';
import { invalidateOrders, orderChanged, reloadOrder } from './queries';
import { NORTE_ORDER } from './test-data';

function seeded() {
  const queryClient = new QueryClient();
  queryClient.setQueryData(getGetComparsaOrderQueryKey(NORTE_ORDER.id), { data: NORTE_ORDER, status: 200 });
  queryClient.setQueryData(getGetComparsaOrdersOverviewQueryKey(), { data: {}, status: 200 });
  queryClient.setQueryData(getGetComparsaOrdersOverviewQueryKey({ editionId: 'e-2030' }), {
    data: {},
    status: 200,
  });
  queryClient.setQueryData(['/api/arquebusiers'], { data: [], status: 200 });
  return queryClient;
}

const invalidated = (queryClient: QueryClient, key: readonly unknown[]) =>
  queryClient.getQueryState(key)?.isInvalidated ?? false;

describe('orders queries', () => {
  it('caches a changed order as the GET response, and refetches the overviews', async () => {
    const queryClient = seeded();
    const changed = { ...NORTE_ORDER, version: 13, status: 'SUBMITTED' as const };

    await orderChanged(queryClient, changed);

    expect(queryClient.getQueryData(getGetComparsaOrderQueryKey(NORTE_ORDER.id))).toMatchObject({
      data: changed,
      status: 200,
    });
    expect(invalidated(queryClient, getGetComparsaOrdersOverviewQueryKey())).toBe(true);
    expect(invalidated(queryClient, getGetComparsaOrdersOverviewQueryKey({ editionId: 'e-2030' }))).toBe(
      true,
    );
    expect(invalidated(queryClient, ['/api/arquebusiers'])).toBe(false);
  });

  it('reloads an out-of-date order and the overviews', async () => {
    const queryClient = seeded();

    await reloadOrder(queryClient, NORTE_ORDER.id);

    expect(invalidated(queryClient, getGetComparsaOrderQueryKey(NORTE_ORDER.id))).toBe(true);
    expect(invalidated(queryClient, getGetComparsaOrdersOverviewQueryKey())).toBe(true);
    expect(invalidated(queryClient, ['/api/arquebusiers'])).toBe(false);
  });

  it('invalidates every order and overview after a registry change, and nothing else of the registry', async () => {
    const queryClient = seeded();

    await invalidateOrders(queryClient);

    expect(invalidated(queryClient, getGetComparsaOrderQueryKey(NORTE_ORDER.id))).toBe(true);
    expect(invalidated(queryClient, getGetComparsaOrdersOverviewQueryKey({ editionId: 'e-2030' }))).toBe(
      true,
    );
    expect(invalidated(queryClient, ['/api/arquebusiers'])).toBe(false);
  });
});
