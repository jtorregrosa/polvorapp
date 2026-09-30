import type { AccountResponse } from '@/api/generated/model';

/** Synthetic signed-in users for component tests (never real people). */
export const SYNTHETIC_ADMIN: AccountResponse = {
  id: '00000000-0000-4000-8000-000000000001',
  name: 'Admin Sintética',
  email: 'admin@polvorapp.example',
  role: 'ADMIN',
  locale: 'es-ES',
  recoveryCodesLeft: 10,
};

export const SYNTHETIC_FIRING_CHIEF: AccountResponse = {
  ...SYNTHETIC_ADMIN,
  id: '00000000-0000-4000-8000-000000000002',
  name: 'Jefe Sintético',
  email: 'jefe@polvorapp.example',
  role: 'FIRING_CHIEF',
};
