import type { BillingConcept, BillingState, BillingSummaryResponse } from '@/api/generated/model';

/** Synthetic edition prices for component tests (the spec's example: 55.00, 4.50, 30.00 and 6.00). */
export const PRICES: Readonly<Record<BillingConcept, number>> = {
  POWDER: 55,
  CAPS: 4.5,
  WEAPON_RENTAL: 30,
  FLASK_RENTAL: 6,
};

export interface Quantities {
  powderKg: number;
  capsBoxes: number;
  weaponRentals: number;
  flaskRentals: number;
}

/** Rounds to cents, as the API writes amounts. */
const round2 = (value: number): number => Math.round(value * 100) / 100;

/**
 * A billing summary as the API returns it, priced with {@link PRICES}; the concepts in `missing`
 * have no price, so their lines have no amount and the summary no total.
 */
export function billingOf(
  quantities: Quantities,
  state: BillingState = 'PROVISIONAL',
  missing: readonly BillingConcept[] = [],
): BillingSummaryResponse {
  const counts: [BillingConcept, number][] = [
    ['POWDER', quantities.powderKg],
    ['CAPS', quantities.capsBoxes],
    ['WEAPON_RENTAL', quantities.weaponRentals],
    ['FLASK_RENTAL', quantities.flaskRentals],
  ];
  const lines = counts.map(([concept, quantity]) => {
    const unitPrice = missing.includes(concept) ? null : PRICES[concept];
    return { concept, quantity, unitPrice, amount: unitPrice === null ? null : round2(quantity * unitPrice) };
  });
  const amounts = lines.map((line) => line.amount);
  // A missing price is never read as zero: any null amount means no total.
  const total = amounts.every((amount) => amount !== null)
    ? round2(amounts.reduce((sum, amount) => sum + amount, 0))
    : null;
  return { lines, total, state, missingPrices: [...missing] };
}

/** The spec's example: 5 kg, 3 caps boxes, 2 weapon and 2 flask rentals; 360.50. */
export const EXAMPLE = billingOf({ powderKg: 5, capsBoxes: 3, weaponRentals: 2, flaskRentals: 2 });
