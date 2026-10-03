import { z } from 'zod';
import { INCOMPLETE_DATE } from '@/components/app/DateInput';
import { INCOMPLETE_TIME } from '@/components/app/TimeInput';
import { isIsoDate } from '@/lib/dates';

/** As the server: `DistributionDay.LocationMaxLength`. */
export const MAX_LOCATION_LENGTH = 200;

const TIME = /^([01]\d|2[0-3]):[0-5]\d$/;

/** Zod messages are translation keys; FormField translates them. */
const messages = {
  required: 'distribution:fields.required',
  tooLong: 'distribution:fields.tooLong',
  date: 'distribution:fields.date',
  time: 'distribution:fields.time',
} as const;

/** A distribution day's date and location (spec: Distribution days). */
export const daySchema = z
  .object({ date: z.string(), location: z.string() })
  .superRefine((values, context) => {
    if (values.date === '') {
      context.addIssue({ code: 'custom', path: ['date'], message: messages.required });
    } else if (values.date === INCOMPLETE_DATE || !isIsoDate(values.date)) {
      context.addIssue({ code: 'custom', path: ['date'], message: messages.date });
    }
    // As the server measures it: normalised, then trimmed.
    const location = values.location.normalize('NFC').trim();
    if (location === '') {
      context.addIssue({ code: 'custom', path: ['location'], message: messages.required });
    } else if (location.length > MAX_LOCATION_LENGTH) {
      context.addIssue({ code: 'custom', path: ['location'], message: messages.tooLong });
    }
  })
  .transform((values) => ({ date: values.date, location: values.location.normalize('NFC').trim() }));

export type DayValues = z.input<typeof daySchema>;
export type DayInput = z.output<typeof daySchema>;

export const EMPTY_DAY: DayValues = { date: '', location: '' };

/** One row of the slots panel: a comparsa and its start time, empty for no slot. */
const slotRow = z
  .object({ comparsaId: z.string(), comparsaName: z.string(), startsAt: z.string() })
  .superRefine((row, context) => {
    if (row.startsAt !== '' && (row.startsAt === INCOMPLETE_TIME || !TIME.test(row.startsAt))) {
      context.addIssue({ code: 'custom', path: ['startsAt'], message: messages.time });
    }
  });

/** A distribution day's slots, saved as one set (spec: Distribution slots). */
export const slotsSchema = z.object({ slots: z.array(slotRow) });

export type SlotsValues = z.input<typeof slotsSchema>;
export type SlotsInput = z.output<typeof slotsSchema>;

const requiredText = z.string().min(1, messages.required);

/** A pickup proxy to register (spec: Pickup proxies): the server checks the rules. */
export const proxySchema = z.object({
  comparsaId: requiredText,
  type: z.enum(['', 'POWDER', 'WEAPONS']).refine((type) => type !== '', messages.required),
  holderEntryId: requiredText,
  proxyEntryId: requiredText,
});

export type ProxyValues = z.input<typeof proxySchema>;
export type ProxyInput = z.output<typeof proxySchema>;
