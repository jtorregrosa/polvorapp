import { useTranslation } from 'react-i18next';
import type { Control } from 'react-hook-form';
import { Side } from '@/api/generated/model';
import { FormField } from '@/components/app/FormField';
import { RadioCards } from '@/components/app/RadioCards';
import { TextInput } from '@/components/app/TextInput';
import { MAX_TEXT_LENGTH } from '../problems';
import type { ComparsaValues } from './comparsaSchema';

/** Name and side of a comparsa, in the create form and the edit panel (spec: Form fields). */
export function ComparsaFields({ control }: { control: Control<ComparsaValues> }) {
  const { t } = useTranslation('catalog');
  return (
    <>
      <FormField control={control} name="name" label={t('comparsas.form.name')} width="name">
        {(field) => <TextInput autoComplete="off" maxLength={MAX_TEXT_LENGTH} {...field} />}
      </FormField>
      <FormField control={control} name="side" label={t('comparsas.form.side')}>
        {(field) => (
          <RadioCards
            {...field}
            options={Object.values(Side).map((value) => ({ value, label: t(`side.${value}`) }))}
          />
        )}
      </FormField>
    </>
  );
}
