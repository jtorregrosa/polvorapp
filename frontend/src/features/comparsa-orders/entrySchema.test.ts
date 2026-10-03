import { describe, expect, it } from 'vitest';
import {
  entryContextOf,
  entrySchema,
  entryValuesOf,
  LoanMode,
  reserveValues,
  toEntryRequest,
  type EntryContext,
  type EntryValues,
} from './entrySchema';
import { messages } from './problems';
import { NORTE_ORDER } from './test-data';

const OWN_WEAPON = '0193a400-0000-7000-8000-000000000001';
const OFFERED = '0193a200-0000-7000-8000-000000000005';
const NOT_OFFERED = '0193a200-0000-7000-8000-000000000008';
const LENT_WEAPON = '0193a400-0000-7000-8000-000000000003';

const CONTEXT: EntryContext = {
  ownedWeaponIds: [OWN_WEAPON],
  offeredModelIds: [OFFERED],
  keepsRemovedWeapon: false,
  hasLoan: false,
};

const ACTIVE: EntryValues = {
  version: '1',
  status: 'ACTIVE',
  powderKg: '2',
  capsBoxes: '0',
  capsType: '',
  weaponSource: 'NONE',
  ownedWeaponId: '',
  rentalWeaponModelId: '',
  loanMode: '',
  loanOwnedWeaponId: '',
  lenderFirstName: '',
  lenderLastName: '',
  lenderNationalId: '',
  lenderWeaponModelId: '',
  lenderWeaponNumber: '',
  lenderOwnershipGuideNumber: '',
  flask: 'NONE',
};

const EXTERNAL: Partial<EntryValues> = {
  weaponSource: 'LOAN',
  loanMode: LoanMode.EXTERNAL,
  lenderFirstName: 'Propietaria',
  lenderLastName: 'Externa Sintética',
  lenderNationalId: '00000092-t',
  lenderWeaponModelId: OFFERED,
  lenderWeaponNumber: 'EXT-0001',
  lenderOwnershipGuideNumber: 'sint-ext-0001',
};

/** The errors of `changes` on an active entry, by field. */
function errors(changes: Partial<EntryValues>, context: EntryContext = CONTEXT): Record<string, string> {
  const result = entrySchema(context).safeParse({ ...ACTIVE, ...changes });
  return result.success
    ? {}
    : Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.'), issue.message]));
}

describe('entrySchema', () => {
  it('accepts an active entry without powder or without a weapon', () => {
    expect(errors({ powderKg: '0' })).toEqual({});
    expect(errors({ weaponSource: 'NONE', powderKg: '2' })).toEqual({});
  });

  it('requires the status, powder, caps, weapon source and flask', () => {
    expect(errors({ status: '', powderKg: '', capsBoxes: '', weaponSource: '', flask: '' })).toEqual({
      status: messages.choice,
      powderKg: messages.choice,
      capsBoxes: messages.required,
      weaponSource: messages.choice,
      flask: messages.choice,
    });
  });

  it('keeps powder to 0, 1 or 2 kg and caps to 0 to 99 boxes (BR-05)', () => {
    expect(errors({ powderKg: '3', capsBoxes: '100' })).toEqual({
      powderKg: messages.outOfRange,
      capsBoxes: messages.outOfRange,
    });
    expect(errors({ capsBoxes: '-1' })).toEqual({ capsBoxes: messages.outOfRange });
    expect(errors({ capsBoxes: '1.5' })).toEqual({ capsBoxes: messages.outOfRange });
  });

  it('needs the caps type with boxes, and ignores it without them', () => {
    expect(errors({ capsBoxes: '3' })).toEqual({ capsType: messages.capsType });
    expect(errors({ capsBoxes: '3', capsType: 'SMALL' })).toEqual({});
    expect(toEntryRequest({ ...ACTIVE, capsBoxes: '0', capsType: 'SMALL' }, 1).capsType).toBeNull();
  });

  it('refuses powder, caps, a weapon or a flask on a reserve entry (BR-05)', () => {
    expect(
      errors({
        status: 'RESERVE',
        powderKg: '1',
        capsBoxes: '2',
        capsType: 'NORMAL',
        weaponSource: 'RENTAL',
        rentalWeaponModelId: OFFERED,
        flask: 'OWNED',
      }),
    ).toEqual({
      powderKg: messages.reserve,
      capsBoxes: messages.reserve,
      weaponSource: messages.reserve,
      flask: messages.reserve,
    });
  });

  it('clears every dependent field when the entry becomes a reserve', () => {
    const reserve = reserveValues({
      ...ACTIVE,
      ...EXTERNAL,
      capsBoxes: '2',
      capsType: 'NORMAL',
      flask: 'RENTAL_2KG',
    });

    expect(errors(reserve)).toEqual({});
    expect(toEntryRequest(reserve, 7)).toEqual({
      version: 7,
      status: 'RESERVE',
      powderKg: 0,
      capsBoxes: 0,
      capsType: null,
      weaponSource: 'NONE',
      ownedWeaponId: null,
      rentalWeaponModelId: null,
      loan: null,
      flask: 'NONE',
    });
  });

  it('takes only the arquebusier own weapons', () => {
    expect(errors({ weaponSource: 'OWNED' })).toEqual({ ownedWeaponId: messages.choice });
    expect(errors({ weaponSource: 'OWNED', ownedWeaponId: LENT_WEAPON })).toEqual({
      ownedWeaponId: messages.choice,
    });
    expect(errors({ weaponSource: 'OWNED', ownedWeaponId: OWN_WEAPON })).toEqual({});
  });

  it('keeps an owned weapon that left the registry when none is chosen', () => {
    expect(errors({ weaponSource: 'OWNED' }, { ...CONTEXT, keepsRemovedWeapon: true })).toEqual({});
    expect(toEntryRequest({ ...ACTIVE, weaponSource: 'OWNED' }, 1).ownedWeaponId).toBeNull();
  });

  it('rents only models offered in the edition (BR-07)', () => {
    expect(errors({ weaponSource: 'RENTAL' })).toEqual({ rentalWeaponModelId: messages.choice });
    expect(errors({ weaponSource: 'RENTAL', rentalWeaponModelId: NOT_OFFERED })).toEqual({
      rentalWeaponModelId: messages.choice,
    });
    expect(errors({ weaponSource: 'RENTAL', rentalWeaponModelId: OFFERED })).toEqual({});
  });

  it('needs a lender for a loan, and keeps the current one when it exists', () => {
    expect(errors({ weaponSource: 'LOAN' })).toEqual({ lenderNationalId: messages.lookUpFirst });
    expect(errors({ weaponSource: 'LOAN', loanMode: LoanMode.KEEP })).toEqual({
      lenderNationalId: messages.lookUpFirst,
    });
    expect(errors({ weaponSource: 'LOAN', loanMode: LoanMode.KEEP }, { ...CONTEXT, hasLoan: true })).toEqual(
      {},
    );
    expect(toEntryRequest({ ...ACTIVE, weaponSource: 'LOAN', loanMode: LoanMode.KEEP }, 1).loan).toBeNull();
  });

  it('borrows a registered owner weapon, never the arquebusier own', () => {
    expect(errors({ weaponSource: 'LOAN', loanMode: LoanMode.REGISTERED })).toEqual({
      loanOwnedWeaponId: messages.choice,
    });
    expect(
      errors({ weaponSource: 'LOAN', loanMode: LoanMode.REGISTERED, loanOwnedWeaponId: OWN_WEAPON }),
    ).toEqual({ loanOwnedWeaponId: messages.ownWeapon });
    expect(
      toEntryRequest(
        { ...ACTIVE, weaponSource: 'LOAN', loanMode: LoanMode.REGISTERED, loanOwnedWeaponId: LENT_WEAPON },
        1,
      ).loan,
    ).toEqual({
      ownedWeaponId: LENT_WEAPON,
      external: null,
    });
  });

  it('needs every field of an external owner', () => {
    expect(errors({ weaponSource: 'LOAN', loanMode: LoanMode.EXTERNAL })).toEqual({
      lenderFirstName: messages.required,
      lenderLastName: messages.required,
      lenderNationalId: messages.required,
      lenderWeaponModelId: messages.choice,
      lenderWeaponNumber: messages.required,
      lenderOwnershipGuideNumber: messages.required,
    });
    expect(errors(EXTERNAL)).toEqual({});
  });

  it('checks the external owner DNI/NIE and the lengths', () => {
    expect(errors({ ...EXTERNAL, lenderNationalId: '00000092A' })).toEqual({
      lenderNationalId: messages.checkLetter,
    });
    expect(errors({ ...EXTERNAL, lenderNationalId: '1234' })).toEqual({ lenderNationalId: messages.invalid });
    expect(
      errors({ ...EXTERNAL, lenderFirstName: 'a'.repeat(101), lenderWeaponNumber: 'n'.repeat(31) }),
    ).toEqual({
      lenderFirstName: messages.tooLong,
      lenderWeaponNumber: messages.tooLong,
    });
    expect(errors({ ...EXTERNAL, lenderLastName: 'Sin\u200Bespacio' })).toEqual({
      lenderLastName: messages.invalid,
    });
  });

  it('sends the external owner trimmed and ignores the fields of other sources', () => {
    const request = toEntryRequest(
      {
        ...ACTIVE,
        ...EXTERNAL,
        ownedWeaponId: OWN_WEAPON,
        rentalWeaponModelId: OFFERED,
        lenderFirstName: ' Propietaria ',
      },
      3,
    );

    expect(request).toMatchObject({ weaponSource: 'LOAN', ownedWeaponId: null, rentalWeaponModelId: null });
    expect(request.loan?.external).toMatchObject({
      firstName: 'Propietaria',
      nationalId: '00000092-t',
      ownershipGuideNumber: 'sint-ext-0001',
    });
  });

  it('builds the rules context and the values from the order response', () => {
    const [owned, rental, loan, gone] = NORTE_ORDER.entries;
    if (!owned || !rental || !loan || !gone) throw new Error('The synthetic order lacks an entry.');

    expect(entryContextOf(NORTE_ORDER, owned)).toEqual({
      ownedWeaponIds: ['00000000-0000-4000-8000-000000000401'],
      offeredModelIds: NORTE_ORDER.offeredModels.map((model) => model.id),
      keepsRemovedWeapon: false,
      hasLoan: false,
    });
    expect(entryContextOf(NORTE_ORDER, loan).hasLoan).toBe(true);
    expect(entryContextOf(NORTE_ORDER, gone).ownedWeaponIds).toBeNull();
    expect(entryValuesOf(loan)).toMatchObject({
      weaponSource: 'LOAN',
      loanMode: LoanMode.KEEP,
      powderKg: '1',
    });
    expect(errors(entryValuesOf(rental), entryContextOf(NORTE_ORDER, rental))).toEqual({});
  });

  it('reports out-of-range powder only once on a reserve entry, as the server does', () => {
    expect(errors({ status: 'RESERVE', powderKg: '5' })).toEqual({ powderKg: messages.outOfRange });
  });

  it('measures the lender texts in NFC, as the server does', () => {
    const decomposed = 'e\u0301'.repeat(30);
    expect(errors({ ...EXTERNAL, lenderWeaponNumber: decomposed })).toEqual({});
  });
});
