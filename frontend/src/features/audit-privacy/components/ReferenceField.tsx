import type { RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import { Form, FormField } from '@/components/app/FormField';
import { TextInput } from '@/components/app/TextInput';
import { REFERENCE_MAX_LENGTH, type ReferenceForm } from './reference-form';

/**
 * The request reference, with help that says not to type the person's name or DNI/NIE (spec: GDPR
 * request screens): it is kept in the audit log. Pressing Enter only checks it; the dialog's button
 * sends the request.
 */
export function ReferenceField({
  form,
  inputRef,
}: {
  form: ReferenceForm;
  inputRef?: RefObject<HTMLInputElement | null>;
}) {
  const { t } = useTranslation('privacy');
  return (
    <Form form={form} onSubmit={() => undefined} requiredNote={false}>
      <FormField
        control={form.control}
        name="reference"
        label={t('reference.label')}
        description={t('reference.help')}
        width="long"
      >
        {(field) => (
          <TextInput
            {...field}
            ref={(node) => {
              field.ref(node);
              if (inputRef) inputRef.current = node;
            }}
            autoComplete="off"
            maxLength={REFERENCE_MAX_LENGTH}
          />
        )}
      </FormField>
    </Form>
  );
}
