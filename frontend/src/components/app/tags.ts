import type { Side, UserRole, WeaponKind } from '@/api/generated/model';

/** Categorical tone of a tag (refine-navigation-and-lists D5): never a semantic status tone. */
export type TagTone = 'neutral' | 1 | 2 | 3 | 4;

/**
 * The single mapping from fixed values to their tag tone (spec: Tags for fixed values), so each
 * value keeps its tone everywhere. Labels live in the `ui` namespace under `tag.<category>.<VALUE>`.
 */
export const TAG_MAP = {
  side: { MOORISH: 3, CHRISTIAN: 1 },
  role: { ADMIN: 2, FIRING_CHIEF: 4 },
  weaponKind: { TRABUCO: 1, ARCABUZ: 2, PISTOL: 4 },
  /** A yes/no flag, such as the rentable flag of a weapon model: "no" is neutral. */
  yesNo: { YES: 2, NO: 'neutral' },
} as const satisfies {
  side: Record<Side, TagTone>;
  role: Record<UserRole, TagTone>;
  weaponKind: Record<WeaponKind, TagTone>;
  yesNo: Record<'YES' | 'NO', TagTone>;
};

export type TagCategory = keyof typeof TAG_MAP;

/** The tone of `value` in `category`, or `undefined` when the value is not mapped. */
export function tagTone(category: TagCategory, value: string): TagTone | undefined {
  return (TAG_MAP[category] as Readonly<Record<string, TagTone | undefined>>)[value];
}

/** The `yesNo` value of a boolean flag. */
export function yesNo(flag: boolean): 'YES' | 'NO' {
  return flag ? 'YES' : 'NO';
}
