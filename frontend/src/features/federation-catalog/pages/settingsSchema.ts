import { z } from 'zod';
import { messages } from '../problems';

/** Limits of the Federation settings, as the API checks them (spec: Federation settings). */
export const SETTINGS_LIMITS = {
  officialName: 150,
  shortName: 40,
  senderName: 80,
  email: 254,
  website: 200,
} as const;

/** The lead times offered, in days (design D2): 2–14 for the close reminder, 1–14 for milestones. */
export const CLOSE_REMINDER_DAYS = Array.from({ length: 13 }, (_, index) => index + 2);
export const MILESTONE_DAYS = Array.from({ length: 14 }, (_, index) => index + 1);

/** Settings-specific messages; `tooLong` names the limit of each field. */
export const settingsMessages = {
  tooLong: (limit: number) => `catalog:settings.validation.tooLong${limit}`,
  email: 'catalog:settings.validation.email',
  website: 'catalog:settings.validation.website',
  senderName: 'catalog:settings.validation.senderName',
  outOfRange: 'catalog:settings.validation.outOfRange',
} as const;

/** A plain address with a dotted domain, as the API's `IsPlainEmail` accepts it. */
const PLAIN_EMAIL = /^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$/;

// Control and invisible characters (line breaks, bidi marks) are refused by the API in every text.
// eslint-disable-next-line no-control-regex -- the point is to find control characters
const UNPRINTABLE = /[\u0000-\u001f\u007f-\u009f\u200b-\u200f\u2028-\u202e\u2060-\u2064\ufeff]/;

/** Characters a sender name may not hold: address syntax and `@` (design D2). */
const SENDER_FORBIDDEN = /[<>"@]/;

function requiredName(limit: number) {
  return z
    .string()
    .trim()
    .min(1, messages.required)
    .max(limit, settingsMessages.tooLong(limit))
    .refine((value) => !UNPRINTABLE.test(value), messages.invalid);
}

/** An optional address: blank is none (sent as null). */
const optionalEmail = z
  .string()
  .trim()
  .max(SETTINGS_LIMITS.email, settingsMessages.tooLong(SETTINGS_LIMITS.email))
  .refine((value) => value === '' || PLAIN_EMAIL.test(value), settingsMessages.email)
  .transform((value) => (value === '' ? null : value));

/**
 * Whether `value` is a website the API keeps: an absolute https address with a dotted ASCII host,
 * no credentials, written in its canonical form (quotes, spaces or backslashes would be escaped).
 */
export function isWebsite(value: string): boolean {
  if (!value.startsWith('https://') || /[\s"<>\\]/.test(value)) return false;
  try {
    const url = new URL(value);
    return (
      url.protocol === 'https:' &&
      url.hostname.includes('.') &&
      !/^[\d.]+$/.test(url.hostname) &&
      !url.hostname.startsWith('[') &&
      !url.hostname.startsWith('xn--') &&
      !url.hostname.includes('.xn--') &&
      url.username === '' &&
      url.password === '' &&
      (url.href === value || url.href === `${value}/`)
    );
  } catch {
    return false;
  }
}

const optionalWebsite = z
  .string()
  .trim()
  .max(SETTINGS_LIMITS.website, settingsMessages.tooLong(SETTINGS_LIMITS.website))
  .refine((value) => value === '' || isWebsite(value), settingsMessages.website)
  .transform((value) => (value === '' ? null : value));

export const identitySchema = z.object({
  officialNameEs: requiredName(SETTINGS_LIMITS.officialName),
  officialNameCa: requiredName(SETTINGS_LIMITS.officialName),
  shortName: requiredName(SETTINGS_LIMITS.shortName),
  contactEmail: optionalEmail,
  website: optionalWebsite,
});

export const emailsSchema = z.object({
  senderName: requiredName(SETTINGS_LIMITS.senderName).refine(
    (value) => !SENDER_FORBIDDEN.test(value),
    settingsMessages.senderName,
  ),
  replyTo: optionalEmail,
});

function days(allowed: readonly number[]) {
  return z
    .string()
    .refine((value) => allowed.includes(Number(value)), messages.choice)
    .transform(Number);
}

export const ordersSchema = z.object({ closeReminderLeadDays: days(CLOSE_REMINDER_DAYS) });

export const calendarSchema = z.object({ milestoneLeadDays: days(MILESTONE_DAYS) });

export type IdentityValues = z.input<typeof identitySchema>;
export type IdentityInput = z.output<typeof identitySchema>;
export type EmailsValues = z.input<typeof emailsSchema>;
export type EmailsInput = z.output<typeof emailsSchema>;
export type OrdersValues = z.input<typeof ordersSchema>;
export type OrdersInput = z.output<typeof ordersSchema>;
export type CalendarValues = z.input<typeof calendarSchema>;
export type CalendarInput = z.output<typeof calendarSchema>;
