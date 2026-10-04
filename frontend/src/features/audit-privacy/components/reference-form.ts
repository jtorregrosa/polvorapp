import { zodResolver } from '@hookform/resolvers/zod';
import type { UseFormReturn } from 'react-hook-form';
import { z } from 'zod';
import { useAppForm } from '@/components/app/use-app-form';

/** The API's limit for a request reference (spec: GDPR request reference). */
export const REFERENCE_MAX_LENGTH = 50;

const schema = z.object({
  reference: z
    .string()
    .trim()
    .min(1, 'privacy:fields.reference.required')
    .max(REFERENCE_MAX_LENGTH, 'privacy:fields.reference.tooLong')
    .regex(/^[^\r\n]*$/, 'privacy:fields.reference.invalid'),
});

export type ReferenceValues = z.infer<typeof schema>;
export type ReferenceForm = UseFormReturn<ReferenceValues>;

/** The request reference's form; the API also refuses one holding an email or a DNI/NIE. */
export function useReferenceForm(): ReferenceForm {
  return useAppForm<ReferenceValues>({ resolver: zodResolver(schema), defaultValues: { reference: '' } });
}
