import type { OrderResponse } from '@/api/generated/model';

/** Whether the user has any status move on the order (see `OrderActions`), so a bar for them is worth showing. */
export function hasOrderActions(order: OrderResponse, isAdmin: boolean): boolean {
  const draftOrReturned = order.status === 'DRAFT' || order.status === 'RETURNED';
  // Submitted or validated orders have only Admin moves (validate, return).
  return draftOrReturned ? isAdmin || order.canEdit : isAdmin;
}
