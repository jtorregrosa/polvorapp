import { describe, expect, it } from 'vitest';
import { changeOf, editionsWithOrders, shareOf } from './trends';

const row = (year: number, active: number, reserve = 0) => ({ year, provisional: false, active, reserve });

describe('trends helpers', () => {
  it('charts only the editions with entries, in their order', () => {
    expect(
      editionsWithOrders([row(2028, 0), row(2029, 0, 2), row(2030, 5), row(2031, 0)]).map((r) => r.year),
    ).toEqual([2029, 2030]);
  });

  it('states a change only against a previous non-zero figure', () => {
    expect(changeOf(412, undefined)).toEqual({ kind: 'alone' });
    expect(changeOf(412, 0)).toEqual({ kind: 'alone' });
    expect(changeOf(400, 400)).toEqual({ kind: 'same' });
    expect(changeOf(412, 400)).toEqual({ kind: 'up', ratio: 0.03 });
    expect(changeOf(380, 400)).toEqual({ kind: 'down', ratio: 0.05 });
  });

  it('gives a share of a total, and 0 of nothing', () => {
    expect(shareOf(27, 100)).toBe(0.27);
    expect(shareOf(3, 0)).toBe(0);
  });
});
