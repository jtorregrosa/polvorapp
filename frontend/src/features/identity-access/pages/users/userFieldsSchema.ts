import { z } from 'zod';
import { UserRole } from '@/api/generated/model';
import { SUPPORTED_LANGUAGES } from '@/i18n/config';
import { messages, requiredText } from '../../problems';

/** The longest name the API accepts. */
export const MAX_NAME_LENGTH = 200;

/** Name, role and email language of a user, as the invitation and edit forms validate them. */
export const userFieldsSchema = z.object({
  name: requiredText.max(MAX_NAME_LENGTH, messages.nameTooLong),
  role: z.enum(UserRole, messages.choice),
  locale: z.enum(SUPPORTED_LANGUAGES, messages.choice),
});
export type UserFieldValues = z.infer<typeof userFieldsSchema>;
