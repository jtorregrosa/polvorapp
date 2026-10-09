import { describe, expect, it } from 'vitest';
import { CAPTURE_PACKAGE, CAPTURED, SERVER_HANDOVER } from '../test-data';
import { captureHolders, flaskTakenBy, groupHolders, rentsFlask, searchHolders } from './holders';

const [ABAD, BERNABEU, CLIMENT] = CAPTURE_PACKAGE.rows;

describe('capture holders (distribution spec: Handover screens)', () => {
  it("marks each holder by the device's capture first, then by what the server recorded", () => {
    const holders = captureHolders(CAPTURE_PACKAGE.rows, [SERVER_HANDOVER], [CAPTURED]);

    expect(holders.map((h) => h.state)).toEqual(['PENDING', 'SYNCED', 'TO_DELIVER']);
    expect(captureHolders(CAPTURE_PACKAGE.rows, [], [{ ...CAPTURED, state: 'conflict' }])[0]?.state).toBe(
      'CONFLICT',
    );
  });

  it('finds holders by exact number, by name in any order without accents, and by DNI/NIE', () => {
    const holders = captureHolders(CAPTURE_PACKAGE.rows, [], []);

    expect(searchHolders(holders, '2').map((h) => h.row.number)).toEqual([2]);
    expect(searchHolders(holders, 'carla climent').map((h) => h.row.number)).toEqual([3]);
    expect(searchHolders(holders, 'CLIMÉNT').map((h) => h.row.number)).toEqual([3]);
    expect(searchHolders(holders, '00000002w').map((h) => h.row.number)).toEqual([2]);
    expect(searchHolders(holders, '02').map((h) => h.row.number)).toEqual([2]);
    expect(searchHolders(holders, '0000000').map((h) => h.row.number)).toEqual([1, 2, 3]);
    expect(searchHolders(holders, '  ')).toHaveLength(3);
  });

  it('groups consecutive holders of the same slot and comparsa', () => {
    const groups = groupHolders(captureHolders(CAPTURE_PACKAGE.rows, [], []));

    expect(groups.map((g) => [g.slot, g.comparsaName, g.holders.length])).toEqual([
      ['09:00', ABAD?.comparsaName, 2],
      ['09:30', CLIMENT?.comparsaName, 1],
    ]);
  });

  it('knows who rents a flask', () => {
    expect([ABAD, BERNABEU, CLIMENT].map((row) => row && rentsFlask(row))).toEqual([true, false, true]);
  });

  it("names the holder who already took a flask, ignoring case and the holder's own", () => {
    const holders = captureHolders(
      CAPTURE_PACKAGE.rows,
      [{ ...SERVER_HANDOVER, rentalFlaskNumber: 'P-300' }],
      [CAPTURED],
    );

    expect(flaskTakenBy('p-117', CLIMENT?.entryId ?? '', holders)).toBe(1);
    expect(flaskTakenBy('P-300', CLIMENT?.entryId ?? '', holders)).toBe(2);
    expect(flaskTakenBy('P-117', ABAD?.entryId ?? '', holders)).toBeUndefined();
    expect(flaskTakenBy('P-999', CLIMENT?.entryId ?? '', holders)).toBeUndefined();
  });
});
