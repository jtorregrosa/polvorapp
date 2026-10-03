import { z } from 'zod';
import {
  ArquebusierStatus,
  CapsType,
  FlaskOption,
  WeaponSource,
  type EditEntryRequest,
  type EntryResponse,
  type LoanRequest,
  type OrderResponse,
} from '@/api/generated/model';
import { parseNationalId } from '@/features/arquebusier-registry/nationalId';
import { messages } from './problems';

/** Limits of the API (EditionEntry.MaxPowderKg and friends, design D2). */
export const MAX_POWDER_KG = 2;
export const MAX_CAPS_BOXES = 99;
export const MAX_NAME_LENGTH = 100;
export const MAX_NUMBER_LENGTH = 30;

/** How the entry's loan is given: kept as it is, a registered owner's weapon, or an external owner. */
export const LoanMode = { KEEP: 'KEEP', REGISTERED: 'REGISTERED', EXTERNAL: 'EXTERNAL' } as const;
export type LoanMode = (typeof LoanMode)[keyof typeof LoanMode];

/** What the rules need to know about the entry (EntryContext on the server). */
export interface EntryContext {
  /** The arquebusier's owned weapons; null once they are no longer in the registry. */
  ownedWeaponIds: readonly string[] | null;
  /** The models offered for rental in the edition (BR-07). */
  offeredModelIds: readonly string[];
  /** The entry is OWNED and its weapon left the registry: it may stay so without a weapon. */
  keepsRemovedWeapon: boolean;
  /** The entry has a loan, which may be kept as it is. */
  hasLoan: boolean;
}

/** The rules' context for an entry of the order, from what the order response carries (design D6). */
export function entryContextOf(order: OrderResponse, entry: EntryResponse): EntryContext {
  return {
    ownedWeaponIds: entry.arquebusier.inRegistry
      ? entry.ownedWeapons.flatMap((weapon) => (weapon.id === null ? [] : [weapon.id]))
      : null,
    offeredModelIds: order.offeredModels.map((model) => model.id),
    keepsRemovedWeapon: entry.weaponSource === WeaponSource.OWNED && entry.ownedWeapon?.removed === true,
    hasLoan: entry.loan !== null,
  };
}

const SHAPE = {
  /** The entry's version when the panel opened or was reset: values and version travel together. */
  version: z.string(),
  status: z.string(),
  powderKg: z.string(),
  capsBoxes: z.string(),
  capsType: z.string(),
  weaponSource: z.string(),
  ownedWeaponId: z.string(),
  rentalWeaponModelId: z.string(),
  loanMode: z.string(),
  loanOwnedWeaponId: z.string(),
  lenderFirstName: z.string(),
  lenderLastName: z.string(),
  lenderNationalId: z.string(),
  lenderWeaponModelId: z.string(),
  lenderWeaponNumber: z.string(),
  lenderOwnershipGuideNumber: z.string(),
  flask: z.string(),
};

export type EntryValues = { [K in keyof typeof SHAPE]: string };
type Field = keyof EntryValues;

const STATUSES: readonly string[] = Object.values(ArquebusierStatus);
const CAPS_TYPES: readonly string[] = Object.values(CapsType);
const SOURCES: readonly string[] = Object.values(WeaponSource);
const FLASKS: readonly string[] = Object.values(FlaskOption);
const WHOLE = /^[0-9]+$/;
/** What the API refuses in names and numbers (InputFields): control, format, private-use, unassigned, separators. */
const UNPRINTABLE = /[\p{Cc}\p{Cf}\p{Co}\p{Cn}\p{Cs}\p{Zl}\p{Zp}]/u;

/** A whole number from the field's text, or undefined when it is not one. */
export function wholeNumber(text: string): number | undefined {
  const trimmed = text.trim();
  return WHOLE.test(trimmed) ? Number(trimmed) : undefined;
}

/**
 * The entry panel's schema (spec: Edition entries (BR-05, BR-07)): the server's blocking rules, so
 * the panel reports them before submitting. Values of another weapon source are ignored, as the
 * server does. The checks that need other records (the lender's weapon, a registered DNI) stay on
 * the server.
 */
export function entrySchema(context: EntryContext) {
  return z.object(SHAPE).superRefine((values, ctx) => {
    const issue = (path: Field, message: string) => {
      ctx.addIssue({ code: 'custom', path: [path], message });
    };
    const text = (path: Field, value: string, max: number) => {
      // Measured in NFC, as the server does (InputFields.Text).
      const trimmed = value.normalize('NFC').trim();
      if (trimmed === '') issue(path, messages.required);
      else if (trimmed.length > max) issue(path, messages.tooLong);
      else if (UNPRINTABLE.test(trimmed)) issue(path, messages.invalid);
    };

    if (!STATUSES.includes(values.status)) issue('status', messages.choice);
    // Out of range counts as no value, as on the server: no reserve rule is then reported.
    const powderNumber = wholeNumber(values.powderKg);
    const powder = powderNumber !== undefined && powderNumber <= MAX_POWDER_KG ? powderNumber : undefined;
    if (values.powderKg.trim() === '') issue('powderKg', messages.choice);
    else if (powder === undefined) issue('powderKg', messages.outOfRange);
    // Out of range counts as no value, as on the server: the caps type is then not asked for.
    const capsNumber = wholeNumber(values.capsBoxes);
    const caps = capsNumber !== undefined && capsNumber <= MAX_CAPS_BOXES ? capsNumber : undefined;
    if (values.capsBoxes.trim() === '') issue('capsBoxes', messages.required);
    else if (caps === undefined) issue('capsBoxes', messages.outOfRange);
    if (caps !== undefined && caps > 0 && !CAPS_TYPES.includes(values.capsType))
      issue('capsType', messages.capsType);
    if (!SOURCES.includes(values.weaponSource)) issue('weaponSource', messages.choice);
    if (!FLASKS.includes(values.flask)) issue('flask', messages.choice);

    if (values.status === ArquebusierStatus.RESERVE) {
      if (powder !== undefined && powder > 0) issue('powderKg', messages.reserve);
      if (caps !== undefined && caps > 0) issue('capsBoxes', messages.reserve);
      if (SOURCES.includes(values.weaponSource) && values.weaponSource !== WeaponSource.NONE)
        issue('weaponSource', messages.reserve);
      if (FLASKS.includes(values.flask) && values.flask !== FlaskOption.NONE)
        issue('flask', messages.reserve);
    }

    switch (values.weaponSource) {
      case WeaponSource.OWNED:
        if (values.ownedWeaponId === '') {
          if (!context.keepsRemovedWeapon) issue('ownedWeaponId', messages.choice);
        } else if (!context.ownedWeaponIds?.includes(values.ownedWeaponId)) {
          issue('ownedWeaponId', messages.choice);
        }
        break;
      case WeaponSource.RENTAL:
        if (!context.offeredModelIds.includes(values.rentalWeaponModelId))
          issue('rentalWeaponModelId', messages.choice);
        break;
      case WeaponSource.LOAN:
        loanRules(values, context, issue, text);
        break;
    }
  });
}

function loanRules(
  values: EntryValues,
  context: EntryContext,
  issue: (path: Field, message: string) => void,
  text: (path: Field, value: string, max: number) => void,
) {
  switch (values.loanMode) {
    case LoanMode.KEEP:
      if (!context.hasLoan) issue('lenderNationalId', messages.lookUpFirst);
      return;
    case LoanMode.REGISTERED:
      if (values.loanOwnedWeaponId === '') issue('loanOwnedWeaponId', messages.choice);
      else if (context.ownedWeaponIds?.includes(values.loanOwnedWeaponId))
        issue('loanOwnedWeaponId', messages.ownWeapon);
      return;
    case LoanMode.EXTERNAL: {
      text('lenderFirstName', values.lenderFirstName, MAX_NAME_LENGTH);
      text('lenderLastName', values.lenderLastName, MAX_NAME_LENGTH);
      const nationalId = parseNationalId(values.lenderNationalId);
      if ('error' in nationalId) {
        issue(
          'lenderNationalId',
          nationalId.error === 'required'
            ? messages.required
            : nationalId.error === 'checkLetter'
              ? messages.checkLetter
              : messages.invalid,
        );
      }
      if (values.lenderWeaponModelId === '') issue('lenderWeaponModelId', messages.choice);
      text('lenderWeaponNumber', values.lenderWeaponNumber, MAX_NUMBER_LENGTH);
      text('lenderOwnershipGuideNumber', values.lenderOwnershipGuideNumber, MAX_NUMBER_LENGTH);
      return;
    }
    default:
      // No lookup yet: the message goes where the person acts, the DNI/NIE.
      issue('lenderNationalId', messages.lookUpFirst);
  }
}

/** The empty lender fields, for a new loan. */
const NO_LENDER = {
  loanOwnedWeaponId: '',
  lenderFirstName: '',
  lenderLastName: '',
  lenderNationalId: '',
  lenderWeaponModelId: '',
  lenderWeaponNumber: '',
  lenderOwnershipGuideNumber: '',
} satisfies Partial<EntryValues>;

/** The panel's values for an entry: a loan starts as kept. */
export function entryValuesOf(entry: EntryResponse): EntryValues {
  return {
    status: entry.status,
    powderKg: String(entry.powderKg),
    capsBoxes: String(entry.capsBoxes),
    capsType: entry.capsType ?? '',
    weaponSource: entry.weaponSource,
    ownedWeaponId: entry.ownedWeapon?.id ?? '',
    rentalWeaponModelId: entry.rentalWeaponModel?.id ?? '',
    loanMode: entry.loan ? LoanMode.KEEP : '',
    version: String(entry.version),
    ...NO_LENDER,
    flask: entry.flask,
  };
}

/** Choosing RESERVE clears powder, caps, weapon and flask (spec: Orders screens). */
export function reserveValues(values: EntryValues): EntryValues {
  return {
    ...values,
    status: ArquebusierStatus.RESERVE,
    powderKg: '0',
    capsBoxes: '0',
    capsType: '',
    weaponSource: WeaponSource.NONE,
    ownedWeaponId: '',
    rentalWeaponModelId: '',
    loanMode: '',
    ...NO_LENDER,
    flask: FlaskOption.NONE,
  };
}

function loanOf(values: EntryValues): LoanRequest | null {
  switch (values.loanMode) {
    case LoanMode.REGISTERED:
      return { ownedWeaponId: values.loanOwnedWeaponId, external: null };
    case LoanMode.EXTERNAL:
      return {
        ownedWeaponId: null,
        external: {
          firstName: values.lenderFirstName.trim(),
          lastName: values.lenderLastName.trim(),
          nationalId: values.lenderNationalId.trim(),
          weaponModelId: values.lenderWeaponModelId,
          weaponNumber: values.lenderWeaponNumber.trim(),
          ownershipGuideNumber: values.lenderOwnershipGuideNumber.trim(),
        },
      };
    default:
      // Kept: the server keeps the entry's loan when the request sends no lender.
      return null;
  }
}

/** The request body for valid values; fields of another weapon source are not sent. */
export function toEntryRequest(values: EntryValues, version = Number(values.version)): EditEntryRequest {
  const caps = wholeNumber(values.capsBoxes) ?? 0;
  return {
    version,
    status: values.status,
    powderKg: wholeNumber(values.powderKg) ?? 0,
    capsBoxes: caps,
    capsType: caps > 0 ? values.capsType : null,
    weaponSource: values.weaponSource,
    ownedWeaponId:
      values.weaponSource === WeaponSource.OWNED && values.ownedWeaponId !== '' ? values.ownedWeaponId : null,
    rentalWeaponModelId: values.weaponSource === WeaponSource.RENTAL ? values.rentalWeaponModelId : null,
    loan: values.weaponSource === WeaponSource.LOAN ? loanOf(values) : null,
    flask: values.flask,
  };
}

/** API field names (validation errors) mapped to the panel's fields. */
export const ENTRY_FIELDS = {
  status: 'status',
  powderKg: 'powderKg',
  capsBoxes: 'capsBoxes',
  capsType: 'capsType',
  weaponSource: 'weaponSource',
  ownedWeaponId: 'ownedWeaponId',
  rentalWeaponModelId: 'rentalWeaponModelId',
  loan: 'lenderNationalId',
  'loan.ownedWeaponId': 'loanOwnedWeaponId',
  'loan.firstName': 'lenderFirstName',
  'loan.lastName': 'lenderLastName',
  'loan.nationalId': 'lenderNationalId',
  'loan.weaponModelId': 'lenderWeaponModelId',
  'loan.weaponNumber': 'lenderWeaponNumber',
  'loan.ownershipGuideNumber': 'lenderOwnershipGuideNumber',
  flask: 'flask',
} as const satisfies Record<string, Field>;
