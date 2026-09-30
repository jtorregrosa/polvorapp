import { useTranslation } from 'react-i18next';
import type { Control } from 'react-hook-form';
import { Side } from '@/api/generated/model';
import { FormField } from '@/components/app/FormField';
import { SelectInput } from '@/components/app/SelectInput';
import { TextInput } from '@/components/app/TextInput';
import { MAX_TEXT_LENGTH } from '../problems';
import type { ComparsaValues } from './comparsaSchema';

export function ComparsaFields({ control }: { control: Control<ComparsaValues> }) {
  const { t } = useTranslation('catalog');
  return (
    <>
      <FormField control={control} name="name" label={t('comparsas.form.name')} required>
        {(field) => <TextInput autoComplete="off" maxLength={MAX_TEXT_LENGTH} {...field} />}
      </FormField>
      <FormField control={control} name="side" label={t('comparsas.form.side')} required>
        {(field) => (
          <SelectInput
            {...field}
            options={[
              { value: '', label: t('validation.choice') },
              ...Object.values(Side).map((value) => ({ value, label: t(`side.${value}`) })),
            ]}
          />
        )}
      </FormField>
    </>
  );
}
