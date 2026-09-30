import { z } from 'zod';
import { Side } from '@/api/generated/model';
import { messages, requiredText } from '../problems';

/** Name and side of a comparsa, as the create and edit forms validate them (spec: Comparsas). */
export const comparsaSchema = z.object({
  name: requiredText,
  // The select starts empty (''), which is "choose an option" until a side is picked.
  side: z.string().pipe(z.enum(Side, messages.choice)),
});

/** What the form holds (`side` is `''` until chosen). */
export type ComparsaValues = z.input<typeof comparsaSchema>;

/** What a valid form submits. */
export type ComparsaInput = z.output<typeof comparsaSchema>;

export const COMPARSA_FIELDS = ['name', 'side'] as const;

/** A duplicate name is shown on the name field. */
export const COMPARSA_CONFLICTS = { 'comparsas.nameTaken': 'name' } as const;
