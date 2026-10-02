import { formatNumber } from './format';

/** Highest amount the API accepts: `numeric(6,2)` (spec: Edition prices). */
export const MAX_MONEY = 9999.99;

/** What a typed amount is: nothing yet, a valid amount in euros, or why it is not one. */
export type MoneyParse =
  | { kind: 'empty' }
  | { kind: 'amount'; value: number }
  | { kind: 'invalid' }
  | { kind: 'decimals' }
  | { kind: 'outOfRange' };

/** Digits, then optionally one decimal separator (comma or point) and more digits. */
const AMOUNT = /^(\d+)(?:[.,](\d+))?$/;

/**
 * Reads an amount as people type it in any of the three languages: `4,50`, `4.50` or `4` (design
 * D10). Thousands separators are not accepted, so `1.000` is never read as one thousand by mistake;
 * the largest amount has four digits anyway. More than two decimals is refused, never rounded.
 */
export function parseMoney(text: string): MoneyParse {
  const trimmed = text.trim();
  if (trimmed === '') return { kind: 'empty' };
  const match = AMOUNT.exec(trimmed);
  if (!match) return { kind: 'invalid' };
  const [, whole = '', fraction = ''] = match;
  if (fraction.length > 2) return { kind: 'decimals' };
  // Built from its digits, so the number's shortest text is exactly what was typed (design D2).
  const value = Number(`${whole}.${fraction || '0'}`);
  return value > MAX_MONEY ? { kind: 'outOfRange' } : { kind: 'amount', value };
}

/** An amount as an editable text in the UI language: two decimals, no thousands separator (`4,50`). */
export function moneyInputText(value: number | null | undefined, language: string): string {
  if (value === null || value === undefined) return '';
  return formatNumber(value, language, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
    useGrouping: false,
  });
}
