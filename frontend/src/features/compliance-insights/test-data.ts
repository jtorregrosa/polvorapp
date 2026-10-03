import {
  ComplianceWarning,
  type ArquebusierRowResponse,
  type ComplianceStatisticsResponse,
  type ComplianceSummaryResponse,
} from '@/api/generated/model';
import { NORTE, SUR } from '@/features/federation-catalog/test-data';

/** Synthetic insights for component tests (never real people). */

const COUNTS: Record<ComplianceWarning, number> = {
  LICENSE_MISSING: 1,
  LICENSE_PENDING: 2,
  LICENSE_EXPIRED: 3,
  LICENSE_EXPIRING: 12,
  COURSE_MISSING: 4,
  UNDER_AGE: 1,
  ID_PHOTO_MISSING: 5,
  LICENSE_PHOTOS_MISSING: 0,
};

export const SUMMARY: ComplianceSummaryResponse = {
  active: 42,
  reserve: 6,
  withWarnings: 17,
  warnings: Object.values(ComplianceWarning).map((code) => ({ code, count: COUNTS[code] })),
};

export const SUMMARY_UP_TO_DATE: ComplianceSummaryResponse = {
  active: 5,
  reserve: 1,
  withWarnings: 0,
  warnings: Object.values(ComplianceWarning).map((code) => ({ code, count: 0 })),
};

/** Twelve arquebusiers whose license expires soon, in no particular order, plus one without warnings. */
export const EXPIRING_ROWS: ArquebusierRowResponse[] = [
  ...Array.from({ length: 12 }, (_, index): ArquebusierRowResponse => {
    const number = 12 - index;
    return {
      id: `00000000-0000-4000-8000-0000000007${String(number).padStart(2, '0')}`,
      firstName: 'Arcabucero',
      lastName: `Sintético ${String(number).padStart(2, '0')}`,
      nationalId: `0000070${String(number).padStart(2, '0')}X`,
      federationId: 700_000 + number,
      comparsaId: number % 2 === 0 ? NORTE.id : SUR.id,
      comparsaName: number % 2 === 0 ? NORTE.name : SUR.name,
      status: 'ACTIVE',
      licenseStatus: 'VALID',
      // Two by two on the same day (numbers 1 and 2 in January…), so the name breaks the tie.
      licenseExpiresOn: `2027-0${String(Math.ceil(number / 2))}-15`,
      hasIdPhoto: true,
      warnings: ['LICENSE_EXPIRING'],
    };
  }),
  {
    id: '00000000-0000-4000-8000-000000000799',
    firstName: 'Arcabucera',
    lastName: 'Sintética Al Día',
    nationalId: '00000799Z',
    federationId: 700_099,
    comparsaId: NORTE.id,
    comparsaName: NORTE.name,
    status: 'ACTIVE',
    licenseStatus: 'VALID',
    licenseExpiresOn: '2031-01-01',
    hasIdPhoto: true,
    warnings: [],
  },
];

const genders = (male: number, female: number, unspecified: number) => ({ male, female, unspecified });

/** Statistics of two comparsas: 12 arquebusiers, 8 active and 4 reserve. */
export const STATISTICS: ComplianceStatisticsResponse = {
  total: 12,
  active: 8,
  reserve: 4,
  gender: genders(7, 4, 1),
  ageBrackets: [
    { bracket: 'UNDER_25', counts: genders(1, 1, 0) },
    { bracket: 'FROM_25_TO_34', counts: genders(2, 1, 1) },
    { bracket: 'FROM_35_TO_44', counts: genders(2, 1, 0) },
    { bracket: 'FROM_45', counts: genders(2, 1, 0) },
  ],
  course: { done: genders(5, 3, 1), notDone: genders(2, 1, 0) },
  licenses: { valid: 6, expiring: 2, expired: 1, pending: 2, none: 1 },
  ownedWeapons: {
    withWeapon: genders(3, 1, 0),
    withoutWeapon: genders(4, 3, 1),
    byKind: [
      { kind: 'TRABUCO', count: 2 },
      { kind: 'ARCABUZ', count: 3 },
      { kind: 'PISTOL', count: 1 },
    ],
  },
  firstYear: { firstYear: genders(2, 1, 0), notFirstYear: genders(5, 3, 1) },
  comparsas: [
    {
      comparsaId: NORTE.id,
      name: NORTE.name,
      total: 7,
      active: 5,
      reserve: 2,
      gender: genders(4, 2, 1),
      withWarnings: 3,
    },
    {
      comparsaId: SUR.id,
      name: SUR.name,
      total: 5,
      active: 3,
      reserve: 2,
      gender: genders(3, 2, 0),
      withWarnings: 1,
    },
  ],
};

/** No arquebusier matches the filters. */
export const STATISTICS_EMPTY: ComplianceStatisticsResponse = {
  ...STATISTICS,
  total: 0,
  active: 0,
  reserve: 0,
  gender: genders(0, 0, 0),
  ageBrackets: STATISTICS.ageBrackets.map((bracket) => ({ ...bracket, counts: genders(0, 0, 0) })),
  course: { done: genders(0, 0, 0), notDone: genders(0, 0, 0) },
  licenses: { valid: 0, expiring: 0, expired: 0, pending: 0, none: 0 },
  ownedWeapons: {
    withWeapon: genders(0, 0, 0),
    withoutWeapon: genders(0, 0, 0),
    byKind: STATISTICS.ownedWeapons.byKind.map((kind) => ({ ...kind, count: 0 })),
  },
  firstYear: { firstYear: genders(0, 0, 0), notFirstYear: genders(0, 0, 0) },
  comparsas: [],
};
