import { z } from 'zod';
import type { OwnedWeaponResponse } from '@/api/generated/model';
import { messages } from './problems';

/** The API's limit on both numbers (OwnedWeapon.NumberMaxLength). */
export const MAX_NUMBER_LENGTH = 30;

const number = z.string().trim().min(1, messages.required).max(MAX_NUMBER_LENGTH, messages.tooLong);

/** An owned weapon as its form validates it (spec: Owned weapons): a model and both numbers. */
export const ownedWeaponSchema = z.object({
  weaponModelId: z.string().min(1, messages.choice),
  weaponNumber: number,
  ownershipGuideNumber: number,
});

export type OwnedWeaponValues = z.input<typeof ownedWeaponSchema>;

export const EMPTY_OWNED_WEAPON: OwnedWeaponValues = {
  weaponModelId: '',
  weaponNumber: '',
  ownershipGuideNumber: '',
};

export function ownedWeaponValuesOf(weapon: OwnedWeaponResponse): OwnedWeaponValues {
  return {
    weaponModelId: weapon.model.id,
    weaponNumber: weapon.weaponNumber,
    ownershipGuideNumber: weapon.ownershipGuideNumber,
  };
}

/** API field names (validation errors) mapped to the form's fields. */
export const OWNED_WEAPON_FIELDS = {
  weaponModelId: 'weaponModelId',
  weaponNumber: 'weaponNumber',
  ownershipGuideNumber: 'ownershipGuideNumber',
} as const satisfies Record<string, keyof OwnedWeaponValues>;
