import type { ArquebusierRowResponse } from '@/api/generated/model';

/** Lower case without accents, so "garcia" finds "García" and "ÑÚÑEZ" finds "Ñúñez". */
export function searchKey(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase()
    .trim();
}

/** A DNI/NIE as stored: without the spaces, dots or hyphens people type in it ("12.345.678-Z"). */
const compactId = (text: string): string => text.replace(/[\s.-]/g, '').toLowerCase();

/** Whether a row matches an already normalised search term: by name, DNI/NIE or federation id. */
export function matchesSearch(row: ArquebusierRowResponse, term: string): boolean {
  const compactTerm = compactId(term);
  return (
    [
      `${row.firstName} ${row.lastName}`,
      `${row.lastName} ${row.firstName}`,
      row.nationalId,
      String(row.federationId),
    ].some((value) => searchKey(value).includes(term)) ||
    (compactTerm !== '' && compactId(row.nationalId).includes(compactTerm))
  );
}
