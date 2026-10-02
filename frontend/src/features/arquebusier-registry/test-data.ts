import type {
  ArquebusierResponse,
  ArquebusierRowResponse,
  WeaponModelResponse,
  WeaponModelSummary,
} from '@/api/generated/model';
import { NORTE, OESTE, SUR } from '@/features/federation-catalog/test-data';

/** Synthetic registry data for component tests (never real people or real documents). */
export { NORTE, OESTE, SUR };

export const ROW_UNO: ArquebusierRowResponse = {
  id: '00000000-0000-4000-8000-000000000301',
  firstName: 'Arcabucero',
  lastName: 'García Sintético',
  nationalId: '00000001R',
  federationId: 100001,
  comparsaId: NORTE.id,
  comparsaName: NORTE.name,
  status: 'ACTIVE',
  licenseStatus: 'VALID',
  licenseExpiresOn: '2030-03-10',
  hasIdPhoto: true,
  warnings: [],
};

export const ROW_DOS: ArquebusierRowResponse = {
  id: '00000000-0000-4000-8000-000000000302',
  firstName: 'Arcabucera',
  lastName: 'Ñúñez Sintética',
  nationalId: 'X0000002T',
  federationId: 100002,
  comparsaId: SUR.id,
  comparsaName: SUR.name,
  status: 'RESERVE',
  licenseStatus: null,
  licenseExpiresOn: null,
  hasIdPhoto: false,
  warnings: ['LICENSE_MISSING', 'COURSE_MISSING', 'ID_PHOTO_MISSING'],
};

export const ROW_TRES: ArquebusierRowResponse = {
  id: '00000000-0000-4000-8000-000000000303',
  firstName: 'Arcabucero',
  lastName: 'Pérez Sintético',
  nationalId: '00000003A',
  federationId: 100003,
  comparsaId: NORTE.id,
  comparsaName: NORTE.name,
  status: 'ACTIVE',
  licenseStatus: 'EXPIRED',
  licenseExpiresOn: '2025-01-01',
  hasIdPhoto: true,
  warnings: ['LICENSE_EXPIRED'],
};

/** Under 18, without the course, with a valid license that expires within 12 months. */
export const ROW_CUATRO: ArquebusierRowResponse = {
  id: '00000000-0000-4000-8000-000000000304',
  firstName: 'Arcabucera',
  lastName: 'Sánchez Sintética',
  nationalId: '00000004G',
  federationId: 100004,
  comparsaId: SUR.id,
  comparsaName: SUR.name,
  status: 'ACTIVE',
  licenseStatus: 'VALID',
  licenseExpiresOn: '2026-12-31',
  hasIdPhoto: true,
  warnings: ['LICENSE_EXPIRING', 'COURSE_MISSING', 'UNDER_AGE'],
};

export const ARCABUZ: WeaponModelSummary = {
  id: '00000000-0000-4000-8000-000000000501',
  kind: 'ARCABUZ',
  side: 'MOORISH',
  handedness: 'RIGHT',
  size: 'NORMAL',
  label: 'ARCABUZ MORO DIESTRO',
  active: true,
  rentable: true,
};

export const RETIRED: WeaponModelSummary = {
  ...ARCABUZ,
  id: '00000000-0000-4000-8000-000000000502',
  handedness: 'LEFT',
  size: 'SMALL',
  label: 'ARCABUZ MORO ZURDO (PEQUEÑO)',
  active: false,
};

export const PISTOLA: WeaponModelSummary = {
  id: '00000000-0000-4000-8000-000000000503',
  kind: 'PISTOL',
  side: null,
  handedness: null,
  size: null,
  label: 'PISTOLA',
  active: true,
  rentable: false,
};

/** The catalogue list as the weapon-model endpoint returns it. */
export const MODELS: WeaponModelResponse[] = [ARCABUZ, PISTOLA, RETIRED].map((model) => ({
  ...model,
  rentable: model.kind !== 'PISTOL',
}));

export const DETAIL_UNO: ArquebusierResponse = {
  id: ROW_UNO.id,
  comparsaId: NORTE.id,
  comparsaName: NORTE.name,
  comparsaActive: true,
  federationId: ROW_UNO.federationId,
  nationalId: ROW_UNO.nationalId,
  firstName: ROW_UNO.firstName,
  lastName: ROW_UNO.lastName,
  birthDate: '1990-05-01',
  email: 'arcabucero.01@polvorapp.example',
  phone: '+34 600 000 001',
  gender: 'MALE',
  status: 'ACTIVE',
  trainingCompletedOn: '2025-11-15',
  license: { type: 'AE', pending: false, issuedOn: '2025-03-10', expiresOn: '2030-03-10', status: 'VALID' },
  ownedWeapons: [
    {
      id: '00000000-0000-4000-8000-000000000401',
      model: ARCABUZ,
      weaponNumber: '1001',
      ownershipGuideNumber: 'SINT-0001',
      version: 11,
    },
    {
      id: '00000000-0000-4000-8000-000000000402',
      model: RETIRED,
      weaponNumber: '1002',
      ownershipGuideNumber: 'SINT-0002',
      version: 12,
    },
  ],
  photos: { id: null, licenseFront: null, licenseBack: null },
  version: 7,
  age: 36,
  warnings: ['ID_PHOTO_MISSING', 'LICENSE_PHOTOS_MISSING'],
};
