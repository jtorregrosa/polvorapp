/** The Federation's time zone: the API's "today" (FederationCalendar) is the date there. */
const FEDERATION_TIME_ZONE = 'Europe/Madrid';

/** `en-CA` formats dates as `yyyy-MM-dd`. */
const ISO_DATE_IN_MADRID = new Intl.DateTimeFormat('en-CA', {
  timeZone: FEDERATION_TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/**
 * Today as `yyyy-MM-dd` in Europe/Madrid, for "not in the future" bounds: the same date the API
 * compares with, not the UTC date (`toISOString`), which is wrong late in the evening in Spain.
 */
export function todayIso(now: Date = new Date()): string {
  return ISO_DATE_IN_MADRID.format(now);
}

/** Whether `value` is a real calendar date written `yyyy-MM-dd` (four-digit year), as the API expects. */
export function isIsoDate(value: string): boolean {
  const match = ISO_DATE.exec(value);
  if (!match) {
    return false;
  }
  const [year, month, day] = match.slice(1).map(Number) as [number, number, number];
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
}
