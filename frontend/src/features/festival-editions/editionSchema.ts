import { z } from 'zod';
import { INCOMPLETE_DATE } from '@/components/app/DateInput';
import { isIsoDate } from '@/lib/dates';
import { parseMoney } from '@/lib/money';
import { messages } from './problems';

/** The longest milestone title the API accepts (CalendarMilestone.TitleMaxLength). */
export const MAX_TITLE_LENGTH = 100;

const MIN_YEAR = 2000;
const MAX_YEAR = 2100;

type Context = z.core.$RefinementCtx;

/** A required date: present, complete and real; reported once, at its field. */
function requiredDate(value: string, path: string, context: Context): string | undefined {
  if (value === '') {
    context.addIssue({ code: 'custom', path: [path], message: messages.required });
    return undefined;
  }
  if (value === INCOMPLETE_DATE || !isIsoDate(value)) {
    context.addIssue({ code: 'custom', path: [path], message: messages.date });
    return undefined;
  }
  return value;
}

/** An optional date: empty is absent; anything else must be complete and real. */
function optionalDate(value: string, path: string, context: Context): string | null | undefined {
  if (value === '') return null;
  if (value === INCOMPLETE_DATE || !isIsoDate(value)) {
    context.addIssue({ code: 'custom', path: [path], message: messages.date });
    return undefined;
  }
  return value;
}

/** The festival dates within the edition's year and in order (spec: Festival editions (UC-10)). */
function festivalDates(year: number, starts: string, ends: string, context: Context): void {
  const startsOn = requiredDate(starts, 'festivalStartsOn', context);
  const endsOn = requiredDate(ends, 'festivalEndsOn', context);
  const prefix = `${String(year)}-`;
  if (startsOn && !startsOn.startsWith(prefix)) {
    context.addIssue({ code: 'custom', path: ['festivalStartsOn'], message: messages.outsideYear });
  }
  if (endsOn && !endsOn.startsWith(prefix)) {
    context.addIssue({ code: 'custom', path: ['festivalEndsOn'], message: messages.outsideYear });
  } else if (startsOn?.startsWith(prefix) && endsOn && endsOn < startsOn) {
    context.addIssue({ code: 'custom', path: ['festivalEndsOn'], message: messages.beforeStart });
  }
}

/** The year as typed: a whole number from 2000 to 2100 (the API's rule). */
function readYear(text: string, context: Context): number | undefined {
  const trimmed = text.trim();
  if (trimmed === '') {
    context.addIssue({ code: 'custom', path: ['year'], message: messages.required });
    return undefined;
  }
  const year = /^\d{4}$/.test(trimmed) ? Number(trimmed) : Number.NaN;
  if (!(year >= MIN_YEAR && year <= MAX_YEAR)) {
    context.addIssue({ code: 'custom', path: ['year'], message: messages.yearRange });
    return undefined;
  }
  return year;
}

/** The create form: the year and the festival dates (design D5). */
export const createEditionSchema = z
  .object({ year: z.string(), festivalStartsOn: z.string(), festivalEndsOn: z.string() })
  .superRefine((values, context) => {
    const year = readYear(values.year, context);
    if (year === undefined) {
      requiredDate(values.festivalStartsOn, 'festivalStartsOn', context);
      requiredDate(values.festivalEndsOn, 'festivalEndsOn', context);
      return;
    }
    festivalDates(year, values.festivalStartsOn, values.festivalEndsOn, context);
  })
  .transform((values) => ({
    year: Number(values.year.trim()),
    festivalStartsOn: values.festivalStartsOn,
    festivalEndsOn: values.festivalEndsOn,
  }));

export type CreateEditionValues = z.input<typeof createEditionSchema>;
export type CreateEditionInput = z.output<typeof createEditionSchema>;

export const EMPTY_CREATE_EDITION: CreateEditionValues = {
  year: '',
  festivalStartsOn: '',
  festivalEndsOn: '',
};

/**
 * The dates section: the festival dates and the order window. The window is optional in a draft
 * and required once the edition has started (spec: Edition prices, the same rule for the window).
 */
export function datesSchema(year: number, windowRequired: boolean) {
  return z
    .object({
      festivalStartsOn: z.string(),
      festivalEndsOn: z.string(),
      ordersOpenOn: z.string(),
      ordersCloseOn: z.string(),
    })
    .superRefine((values, context) => {
      festivalDates(year, values.festivalStartsOn, values.festivalEndsOn, context);
      const read = windowRequired ? requiredDate : optionalDate;
      const opens = read(values.ordersOpenOn, 'ordersOpenOn', context);
      const closes = read(values.ordersCloseOn, 'ordersCloseOn', context);
      const starts = isIsoDate(values.festivalStartsOn) ? values.festivalStartsOn : undefined;
      if (opens && starts && opens > starts) {
        context.addIssue({ code: 'custom', path: ['ordersOpenOn'], message: messages.afterFestival });
      }
      if (closes && starts && closes > starts) {
        context.addIssue({ code: 'custom', path: ['ordersCloseOn'], message: messages.afterFestival });
      } else if (opens && closes && closes < opens) {
        context.addIssue({ code: 'custom', path: ['ordersCloseOn'], message: messages.beforeOpen });
      }
    })
    .transform((values) => ({
      festivalStartsOn: values.festivalStartsOn,
      festivalEndsOn: values.festivalEndsOn,
      ordersOpenOn: values.ordersOpenOn || null,
      ordersCloseOn: values.ordersCloseOn || null,
    }));
}

export type DatesValues = z.input<ReturnType<typeof datesSchema>>;
export type DatesInput = z.output<ReturnType<typeof datesSchema>>;

export const PRICE_FIELDS = ['powderPerKg', 'capsBox', 'weaponRental', 'flaskRental'] as const;

export type PriceField = (typeof PRICE_FIELDS)[number];

/** One price as typed: empty only when optional; never rounded (design D2, `parseMoney`). */
function price(required: boolean) {
  return z.string().transform((text, context): number | null => {
    const parsed = parseMoney(text);
    switch (parsed.kind) {
      case 'amount':
        return parsed.value;
      case 'empty':
        if (!required) return null;
        context.addIssue({ code: 'custom', message: messages.required });
        return z.NEVER;
      case 'decimals':
        context.addIssue({ code: 'custom', message: messages.decimals });
        return z.NEVER;
      case 'outOfRange':
        context.addIssue({ code: 'custom', message: messages.moneyRange });
        return z.NEVER;
      default:
        context.addIssue({ code: 'custom', message: messages.money });
        return z.NEVER;
    }
  });
}

/**
 * The prices section: each optional in a draft, all required once the edition has started. Nested
 * under `prices`, so its field paths match the API's (`prices.capsBox`).
 */
export function pricesSchema(required: boolean) {
  return z.object({
    prices: z.object({
      powderPerKg: price(required),
      capsBox: price(required),
      weaponRental: price(required),
      flaskRental: price(required),
    }),
  });
}

export type PricesValues = z.input<ReturnType<typeof pricesSchema>>;
export type PricesInput = z.output<ReturnType<typeof pricesSchema>>;

/** The offered models section: the whole set (spec: Rental models offered in an edition (BR-07)). */
export const modelsSchema = z.object({ weaponModelIds: z.array(z.string()) });

export type ModelsValues = z.infer<typeof modelsSchema>;

/** A calendar milestone: a date and a one-line title (spec: Calendar milestones). */
export const milestoneSchema = z
  .object({ date: z.string(), title: z.string() })
  .superRefine((values, context) => {
    requiredDate(values.date, 'date', context);
    // As the server measures it: normalised, then trimmed.
    const title = values.title.normalize('NFC').trim();
    if (title === '') {
      context.addIssue({ code: 'custom', path: ['title'], message: messages.required });
    } else if (title.length > MAX_TITLE_LENGTH) {
      context.addIssue({ code: 'custom', path: ['title'], message: messages.tooLong });
    }
  })
  .transform((values) => ({ date: values.date, title: values.title.normalize('NFC').trim() }));

export type MilestoneValues = z.input<typeof milestoneSchema>;
export type MilestoneInput = z.output<typeof milestoneSchema>;

export const EMPTY_MILESTONE: MilestoneValues = { date: '', title: '' };
