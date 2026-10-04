import type {
  EntryResponse,
  OrderResponse,
  OrderTotalsResponse,
  OverviewResponse,
} from '@/api/generated/model';
import { billingOf } from '@/features/billing/test-data';

/** Synthetic orders for component tests: invented comparsas, people, numbers and dates. */
const NORTE = {
  id: '00000000-0000-4000-8000-000000000101',
  name: 'Comparsa Sintética Norte',
  side: 'CHRISTIAN',
} as const;
const SUR = {
  id: '00000000-0000-4000-8000-000000000102',
  name: 'Comparsa Sintética Sur',
  side: 'MOORISH',
} as const;
const ESTE = {
  id: '00000000-0000-4000-8000-000000000103',
  name: 'Comparsa Sintética Este',
  side: 'CHRISTIAN',
} as const;

export const EDITION_2031 = {
  id: '00000000-0000-4000-8000-000000002031',
  year: 2031,
  status: 'IN_PROGRESS',
  ordersOpen: true,
} as const;

export const ARCABUZ = { id: '00000000-0000-4000-8000-000000000601', label: 'ARCABUZ MORO DIESTRO' } as const;
export const TRABUCO = {
  id: '00000000-0000-4000-8000-000000000602',
  label: 'TRABUCO CRISTIANO DIESTRO',
} as const;

export const NO_TOTALS: OrderTotalsResponse = {
  active: 0,
  reserve: 0,
  powderKg: 0,
  normalCapsBoxes: 0,
  smallCapsBoxes: 0,
  weaponRentals: [],
  flaskRentals1Kg: 0,
  flaskRentals2Kg: 0,
  loans: 0,
  ownedWeapons: 0,
  entriesWithWarnings: 0,
};

const OWN_WEAPON = {
  id: '00000000-0000-4000-8000-000000000401',
  weaponModelId: TRABUCO.id,
  modelLabel: TRABUCO.label,
  weaponNumber: '1001',
  ownershipGuideNumber: null,
  removed: false,
};

const ENTRY: EntryResponse = {
  id: '00000000-0000-4000-8000-000000000801',
  version: 3,
  erased: false,
  arquebusier: {
    id: '00000000-0000-4000-8000-000000000301',
    firstName: 'Arcabucero',
    lastName: 'Sintético Uno',
    nationalId: '00000001R',
    federationId: 100001,
    inRegistry: true,
  },
  status: 'ACTIVE',
  powderKg: 2,
  capsBoxes: 3,
  capsType: 'NORMAL',
  weaponSource: 'OWNED',
  ownedWeapon: OWN_WEAPON,
  rentalWeaponModel: null,
  loan: null,
  flask: 'OWNED',
  warnings: [],
  issues: [],
  firstYear: false,
  ownedWeapons: [OWN_WEAPON],
};

/** Norte's order in the current edition: one entry of each kind the page shows. */
export const NORTE_ORDER: OrderResponse = {
  id: '00000000-0000-4000-8000-000000000701',
  version: 12,
  status: 'DRAFT',
  edition: EDITION_2031,
  comparsa: NORTE,
  preparedAt: '2031-01-15T09:30:00Z',
  submission: null,
  review: null,
  canEdit: true,
  readOnlyReason: null,
  entries: [
    ENTRY,
    {
      ...ENTRY,
      id: '00000000-0000-4000-8000-000000000802',
      arquebusier: {
        ...ENTRY.arquebusier,
        id: '00000000-0000-4000-8000-000000000303',
        lastName: 'Sintético Tres',
        nationalId: '00000003A',
        federationId: 100003,
      },
      powderKg: 2,
      capsBoxes: 0,
      capsType: null,
      weaponSource: 'RENTAL',
      ownedWeapon: null,
      rentalWeaponModel: TRABUCO,
      flask: 'RENTAL_2KG',
      warnings: ['COURSE_MISSING', 'LICENSE_EXPIRED'],
      firstYear: true,
      ownedWeapons: [],
    },
    {
      ...ENTRY,
      id: '00000000-0000-4000-8000-000000000803',
      arquebusier: {
        ...ENTRY.arquebusier,
        id: '00000000-0000-4000-8000-000000000305',
        lastName: 'Sintético Cinco',
        nationalId: '00000005M',
        federationId: 100005,
      },
      powderKg: 1,
      capsBoxes: 1,
      weaponSource: 'LOAN',
      ownedWeapon: null,
      loan: {
        lenderKind: 'ARQUEBUSIER',
        lenderErased: false,
        lenderFirstName: 'Arcabucera',
        lenderLastName: 'Sintética Seis',
        lenderComparsaName: SUR.name,
        ownedWeaponId: '00000000-0000-4000-8000-000000000403',
        weaponModel: ARCABUZ,
        weaponNumber: '1003',
        ownershipGuideNumber: null,
        externalNationalId: null,
        weaponRemoved: false,
      },
      flask: 'RENTAL_1KG',
      firstYear: true,
      ownedWeapons: [],
    },
    {
      ...ENTRY,
      id: '00000000-0000-4000-8000-000000000804',
      arquebusier: {
        id: null,
        firstName: 'Arcabucero',
        lastName: 'Sintético Histórico',
        nationalId: '00000091E',
        federationId: 100091,
        inRegistry: false,
      },
      status: 'RESERVE',
      powderKg: 0,
      capsBoxes: 0,
      capsType: null,
      weaponSource: 'NONE',
      ownedWeapon: null,
      flask: 'NONE',
      firstYear: null,
      ownedWeapons: [],
    },
  ],
  notInOrder: [
    {
      arquebusierId: '00000000-0000-4000-8000-000000000309',
      firstName: 'Arcabucero',
      lastName: 'Sintético Nueve',
      status: 'ACTIVE',
    },
  ],
  lentOut: [
    {
      loanId: '00000000-0000-4000-8000-000000000901',
      weaponModel: TRABUCO,
      weaponNumber: '1001',
      lenderFirstName: 'Arcabucero',
      lenderLastName: 'Sintético Uno',
      borrowerFirstName: 'Arcabucera',
      borrowerLastName: 'Sintética Siete',
      borrowerComparsaName: SUR.name,
      borrowerErased: false,
    },
  ],
  totals: {
    ...NO_TOTALS,
    active: 3,
    reserve: 1,
    powderKg: 5,
    normalCapsBoxes: 4,
    weaponRentals: [{ weaponModel: TRABUCO, count: 1 }],
    flaskRentals1Kg: 1,
    flaskRentals2Kg: 1,
    loans: 1,
    ownedWeapons: 1,
    entriesWithWarnings: 1,
  },
  offeredModels: [ARCABUZ, TRABUCO],
  // 5 kg, 4 caps boxes, 1 weapon and 2 flask rentals: 335.00.
  billing: billingOf({ powderKg: 5, capsBoxes: 4, weaponRentals: 1, flaskRentals: 2 }),
};

/** Sur's submitted order: 3 kg and 2 weapon rentals, 225.00. */
const SUR_BILLING = billingOf({ powderKg: 3, capsBoxes: 0, weaponRentals: 2, flaskRentals: 0 });

/** The Admin's overview of the current edition: one order validated, one submitted, one not prepared. */
export const ADMIN_OVERVIEW: OverviewResponse = {
  edition: EDITION_2031,
  rows: [
    { comparsa: ESTE, orderId: null, status: null, totals: null, canPrepare: true, billing: null },
    {
      comparsa: NORTE,
      orderId: NORTE_ORDER.id,
      status: 'VALIDATED',
      totals: NORTE_ORDER.totals,
      canPrepare: false,
      billing: { ...NORTE_ORDER.billing, state: 'FINAL' },
    },
    {
      comparsa: SUR,
      orderId: '00000000-0000-4000-8000-000000000702',
      status: 'SUBMITTED',
      totals: { ...NO_TOTALS, active: 2, powderKg: 3, weaponRentals: [{ weaponModel: ARCABUZ, count: 2 }] },
      canPrepare: false,
      billing: SUR_BILLING,
    },
  ],
  statusCounts: { notPrepared: 1, draft: 0, submitted: 1, returned: 0, validated: 1 },
  editionTotals: {
    ...NORTE_ORDER.totals,
    active: 5,
    powderKg: 8,
    weaponRentals: [
      { weaponModel: ARCABUZ, count: 2 },
      { weaponModel: TRABUCO, count: 1 },
    ],
  },
  // Both orders together: 560.00, provisional while Sur's is not validated.
  editionBilling: billingOf({ powderKg: 8, capsBoxes: 4, weaponRentals: 3, flaskRentals: 2 }),
};

/** A FiringChief's overview: their two comparsas, one prepared, without Federation figures. */
export const CHIEF_OVERVIEW: OverviewResponse = {
  edition: EDITION_2031,
  rows: [
    {
      comparsa: NORTE,
      orderId: NORTE_ORDER.id,
      status: 'DRAFT',
      totals: NORTE_ORDER.totals,
      canPrepare: false,
      billing: NORTE_ORDER.billing,
    },
    { comparsa: SUR, orderId: null, status: null, totals: null, canPrepare: true, billing: null },
  ],
  statusCounts: null,
  editionTotals: null,
  editionBilling: null,
};

export const NO_EDITION_OVERVIEW: OverviewResponse = {
  edition: null,
  rows: [],
  statusCounts: null,
  editionTotals: null,
  editionBilling: null,
};
