import type { QueryClient } from '@tanstack/react-query';
import {
  getGetComparsaOrderQueryKey,
  getGetComparsaOrdersOverviewQueryKey,
  type getComparsaOrderResponse,
} from '@/api/generated/comparsa-orders/comparsa-orders';
import type { OrderResponse } from '@/api/generated/model';
import { invalidateInsights } from '@/features/compliance-insights/queries';

const ORDERS_PATH = '/api/comparsa-orders';

/**
 * After an order write: the API answers with the whole order (design D6), which replaces the cached
 * one, so the page shows its new status, version and totals at once. The overviews (every edition,
 * by prefix) are refetched for their totals and statuses.
 */
export async function orderChanged(queryClient: QueryClient, order: OrderResponse): Promise<void> {
  // The cache holds orval's whole response, as `GET /comparsa-orders/{id}` returns it.
  queryClient.setQueryData<getComparsaOrderResponse>(getGetComparsaOrderQueryKey(order.id), {
    data: order,
    status: 200,
    headers: new Headers(),
  });
  await queryClient.invalidateQueries({ queryKey: getGetComparsaOrdersOverviewQueryKey() });
}

/**
 * When a write is refused because the order on screen is out of date (orders closed, changed or
 * validated meanwhile): refetches it and the overviews, so the page shows the new state.
 */
export async function reloadOrder(queryClient: QueryClient, orderId: string): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: getGetComparsaOrderQueryKey(orderId) }),
    queryClient.invalidateQueries({ queryKey: getGetComparsaOrdersOverviewQueryKey() }),
  ]);
}

/**
 * After a registry change that orders show (an arquebusier deleted, transferred or edited): their
 * orders and the first-year counts of the statistics may change.
 */
export async function invalidateOrders(queryClient: QueryClient): Promise<void> {
  await Promise.all([
    // Orval keys are whole paths, so every order and overview is matched by the path's start.
    queryClient.invalidateQueries({
      predicate: (query) => {
        const [path] = query.queryKey;
        return typeof path === 'string' && path.startsWith(ORDERS_PATH);
      },
    }),
    invalidateInsights(queryClient),
  ]);
}
