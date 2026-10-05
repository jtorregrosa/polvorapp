import { z } from 'zod';
import { Handedness, Side, WeaponKind, WeaponSize } from '@/api/generated/model';
import { MAX_TEXT_LENGTH, messages } from '../problems';

const ATTRIBUTES = ['side', 'handedness', 'size'] as const;
const KINDS: readonly string[] = Object.values(WeaponKind);

const isKind = (value: string): value is WeaponKind => KINDS.includes(value);

/**
 * A weapon model as the create and edit forms validate it (spec: Weapon models (BR-07)), mirroring
 * the server: every kind but a pistol needs side, handedness and size; any kind may be rentable.
 * Selects hold `''` until chosen; a pistol's empty attributes are submitted as `null`.
 * Every rule is checked in one pass, so all the errors of a submission show at once.
 */
export const weaponModelSchema = z
  .object({
    kind: z.string(),
    side: z.union([z.literal(''), z.enum(Side)]),
    handedness: z.union([z.literal(''), z.enum(Handedness)]),
    size: z.union([z.literal(''), z.enum(WeaponSize)]),
    rentable: z.boolean(),
    label: z.string(),
  })
  .superRefine((model, context) => {
    const label = model.label.trim();
    if (label === '') {
      context.addIssue({ code: 'custom', path: ['label'], message: messages.required });
    } else if (label.length > MAX_TEXT_LENGTH) {
      context.addIssue({ code: 'custom', path: ['label'], message: messages.tooLong });
    }

    if (!isKind(model.kind)) {
      context.addIssue({ code: 'custom', path: ['kind'], message: messages.choice });
      return;
    }
    if (model.kind === WeaponKind.PISTOL) {
      return;
    }
    for (const attribute of ATTRIBUTES) {
      if (model[attribute] === '') {
        context.addIssue({ code: 'custom', path: [attribute], message: messages.choice });
      }
    }
  })
  .transform((model) => ({
    // The refinement above has checked the kind; only valid values get here.
    kind: model.kind as WeaponKind,
    side: model.side || null,
    handedness: model.handedness || null,
    size: model.size || null,
    rentable: model.rentable,
    label: model.label.trim(),
  }));

/** What the form holds. */
export type WeaponModelValues = z.input<typeof weaponModelSchema>;

/** What a valid form submits (the API's request body). */
export type WeaponModelInput = z.output<typeof weaponModelSchema>;

/** Fields the API can name in a `validation` problem and the form shows them on. */
export const WEAPON_MODEL_FIELDS = ['kind', 'side', 'handedness', 'size', 'label'] as const;

/** A duplicate label is shown on the label field; a duplicate combination stays a page message. */
export const WEAPON_MODEL_CONFLICTS = { 'weaponModels.labelTaken': 'label' } as const;

export const EMPTY_WEAPON_MODEL: WeaponModelValues = {
  kind: '',
  side: '',
  handedness: '',
  size: '',
  rentable: false,
  label: '',
};
