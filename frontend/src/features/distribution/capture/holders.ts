import type { CaptureRowResponse, HandoverResponse } from '@/api/generated/model';
import type { CapturedHandover } from '../offline/store';

/** Where a holder's powder stands on this device (design-system: Status semantics, `handover`). */
export type HolderState = 'TO_DELIVER' | 'PENDING' | 'SYNCED' | 'CONFLICT';

/** A holder of the day with what the device knows of their handover. */
export interface CaptureHolder {
  row: CaptureRowResponse;
  state: HolderState;
  /** Captured on this device and not synced (pending or in conflict). */
  captured: CapturedHandover | undefined;
  /** Recorded on the server, as the device last saw it. */
  recorded: HandoverResponse | undefined;
}

/**
 * What the panel was opened for, fixed while it is open: a sync that records the handover meanwhile
 * must not swap the form for the read-only view under the Admin's fingers (WCAG 3.2.2).
 */
export type PanelMode = 'capture' | 'synced';

/** The panel a holder opens: read-only once synced, the form otherwise. */
export const panelModeOf = (holder: CaptureHolder): PanelMode =>
  holder.state === 'SYNCED' && holder.recorded ? 'synced' : 'capture';

/** A slot and comparsa of the day with its holders, in the list's order. */
export interface HolderGroup {
  key: string;
  slot: string | null;
  comparsaName: string;
  holders: CaptureHolder[];
}

/** Each holder's state: the device's own capture first, then what the server recorded. */
export function captureHolders(
  rows: readonly CaptureRowResponse[],
  recorded: readonly HandoverResponse[],
  queue: readonly CapturedHandover[],
): CaptureHolder[] {
  const byHolder = new Map(recorded.map((h) => [h.holderEntryId, h]));
  const capturedBy = new Map(queue.map((h) => [h.holderEntryId, h]));
  return rows.map((row) => {
    const captured = capturedBy.get(row.entryId);
    const synced = byHolder.get(row.entryId);
    const state: HolderState = captured
      ? captured.state === 'conflict'
        ? 'CONFLICT'
        : 'PENDING'
      : synced
        ? 'SYNCED'
        : 'TO_DELIVER';
    return { row, state, captured, recorded: synced };
  });
}

/** Lower case without accents, so "Climent" matches "climént". */
const fold = (text: string) =>
  text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLocaleLowerCase();

/** A day's numbers have at most this many digits: a longer run of digits is a DNI. */
const NUMBER_DIGITS = 3;

/** The holders matching a search by number (exact), name or DNI/NIE (anywhere); all of them for none. */
export function searchHolders(holders: readonly CaptureHolder[], search: string): CaptureHolder[] {
  const query = fold(search.trim());
  if (query.length === 0) return [...holders];
  // Digits are a number ("01" is number 1) or, from more digits than a number has, the start of a
  // DNI being typed.
  if (/^\d+$/.test(query)) {
    const dni = query.length > NUMBER_DIGITS;
    return holders.filter(
      (h) => h.row.number === Number(query) || (dni && fold(h.row.nationalId ?? '').startsWith(query)),
    );
  }
  return holders.filter((h) =>
    [`${h.row.lastName} ${h.row.firstName}`, `${h.row.firstName} ${h.row.lastName}`, h.row.nationalId ?? '']
      .map(fold)
      .some((text) => text.includes(query)),
  );
}

/** Consecutive holders of the same slot and comparsa, as the numbering orders them. */
export function groupHolders(holders: readonly CaptureHolder[]): HolderGroup[] {
  const groups: HolderGroup[] = [];
  for (const holder of holders) {
    const key = `${holder.row.slot ?? ''}|${holder.row.comparsaId}`;
    const last = groups.at(-1);
    if (last?.key === key) {
      last.holders.push(holder);
    } else {
      groups.push({ key, slot: holder.row.slot, comparsaName: holder.row.comparsaName, holders: [holder] });
    }
  }
  return groups;
}

/** Whether the holder's entry rents a flask, which then needs its number (spec: Powder handovers). */
export const rentsFlask = (row: CaptureRowResponse): boolean =>
  row.flask === 'RENTAL_1KG' || row.flask === 'RENTAL_2KG';

/**
 * The number of the holder who already took this flask on the device's knowledge (server handovers
 * and this device's captures), ignoring case; undefined when it is free. The holder's own handover
 * does not count.
 */
export function flaskTakenBy(
  flaskNumber: string,
  holderEntryId: string,
  holders: readonly CaptureHolder[],
): number | undefined {
  const wanted = flaskNumber.trim().toUpperCase();
  if (wanted.length === 0) return undefined;
  return holders.find((h) => {
    if (h.row.entryId === holderEntryId) return false;
    const numbers = [h.captured?.rentalFlaskNumber, h.recorded?.rentalFlaskNumber];
    return numbers.some((n) => n?.trim().toUpperCase() === wanted);
  })?.row.number;
}

/** A holder's or proxy's name as the lists show it: "Last name, First name". */
export const holderName = (row: Pick<CaptureRowResponse, 'lastName' | 'firstName'>): string =>
  [row.lastName, row.firstName].filter((part) => part.length > 0).join(', ');
