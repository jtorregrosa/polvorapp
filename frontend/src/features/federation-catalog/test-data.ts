import type {
  ComparsaResponse,
  FiringChiefResponse,
  UserResponse,
  WeaponModelResponse,
} from '@/api/generated/model';

/** Synthetic catalogue data for component tests (never real comparsas or people). */
export const NORTE: ComparsaResponse = {
  id: '00000000-0000-4000-8000-000000000101',
  name: 'Comparsa Sintética Norte',
  side: 'CHRISTIAN',
  active: true,
};

export const SUR: ComparsaResponse = {
  id: '00000000-0000-4000-8000-000000000102',
  name: 'Comparsa Sintética Sur',
  side: 'MOORISH',
  active: true,
};

export const OESTE: ComparsaResponse = {
  id: '00000000-0000-4000-8000-000000000104',
  name: 'Comparsa Sintética Oeste',
  side: 'MOORISH',
  active: false,
};

export const CHIEF_UNO: UserResponse = {
  id: '00000000-0000-4000-8000-000000000201',
  name: 'Jefe Sintético Uno',
  email: 'jefe.uno@polvorapp.example',
  role: 'FIRING_CHIEF',
  locale: 'es-ES',
  status: 'ACTIVE',
  twoFactorEnabled: true,
  lastSignInAt: null,
  createdAt: '2026-01-10T09:00:00Z',
};

export const CHIEF_DOS: UserResponse = {
  ...CHIEF_UNO,
  id: '00000000-0000-4000-8000-000000000202',
  name: 'Jefa Sintética Dos',
  email: 'jefa.dos@polvorapp.example',
  status: 'INVITED',
  twoFactorEnabled: false,
};

export const CHIEF_BAJA: UserResponse = {
  ...CHIEF_UNO,
  id: '00000000-0000-4000-8000-000000000203',
  name: 'Persona Desactivada',
  email: 'desactivada@polvorapp.example',
  status: 'DEACTIVATED',
};

export const OTRA_ADMIN: UserResponse = {
  ...CHIEF_UNO,
  id: '00000000-0000-4000-8000-000000000204',
  name: 'Otra Admin Sintética',
  email: 'otra.admin@polvorapp.example',
  role: 'ADMIN',
};

export const asFiringChief = (user: UserResponse): FiringChiefResponse => ({
  userId: user.id,
  name: user.name,
  email: user.email,
  status: user.status,
});

export const TRABUCO: WeaponModelResponse = {
  id: '00000000-0000-4000-8000-000000000301',
  kind: 'TRABUCO',
  side: 'CHRISTIAN',
  handedness: 'LEFT',
  size: 'SMALL',
  rentable: true,
  label: 'TRABUCO CRISTIANO ZURDO (PEQUEÑO)',
  active: true,
};

export const PISTOLA: WeaponModelResponse = {
  id: '00000000-0000-4000-8000-000000000309',
  kind: 'PISTOL',
  side: null,
  handedness: null,
  size: null,
  rentable: false,
  label: 'PISTOLA',
  active: true,
};

export const ARCABUZ_RETIRADO: WeaponModelResponse = {
  id: '00000000-0000-4000-8000-000000000308',
  kind: 'ARCABUZ',
  side: 'MOORISH',
  handedness: 'LEFT',
  size: 'SMALL',
  rentable: true,
  label: 'ARCABUZ MORO ZURDO (PEQUEÑO)',
  active: false,
};
