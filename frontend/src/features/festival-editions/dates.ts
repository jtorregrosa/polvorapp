import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useFormatters } from '@/lib/format';

/** A calendar date (`yyyy-MM-dd`) as a Date at noon UTC, so it is the same day in Madrid. */
export function calendarDate(iso: string): Date {
  return new Date(`${iso}T12:00:00Z`);
}

const LONG: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'long', year: 'numeric' };

export interface EditionDates {
  /** One day in words, e.g. "22 de abril de 2031". */
  day: (iso: string) => string;
  /** The festival, from one day to another (or a single day). */
  festival: (from: string, to: string) => string;
}

/** Edition dates in the UI language; stable while the language is, so table columns can depend on it. */
export function useEditionDates(): EditionDates {
  const { t } = useTranslation('editions');
  const { date } = useFormatters();
  return useMemo(() => {
    const day = (iso: string) => date(calendarDate(iso), LONG);
    return {
      day,
      festival: (from, to) =>
        from === to ? day(from) : t('list.festivalDates', { from: day(from), to: day(to) }),
    };
  }, [t, date]);
}
