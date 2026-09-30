import { useTranslation } from 'react-i18next';
import type { Control } from 'react-hook-form';
import { FormField } from '@/components/app/FormField';
import { SelectInput } from '@/components/app/SelectInput';
import { TextInput } from '@/components/app/TextInput';
import { SUPPORTED_LANGUAGES } from '@/i18n/config';
import { MAX_NAME_LENGTH, type UserFieldValues } from './userFieldsSchema';

/**
 * Name, role and email language: shared by the invitation and the user detail forms, whose values
 * include at least these fields.
 */
export function UserFields<TValues extends UserFieldValues>({
  control: formControl,
}: {
  control: Control<TValues>;
}) {
  // Sound narrowing: TValues has every field used here (a Control is invariant in its values type).
  const control = formControl as unknown as Control<UserFieldValues>;
  const { t } = useTranslation('identity');
  const { t: tCommon } = useTranslation();

  return (
    <>
      <FormField control={control} name="name" label={t('users.fields.name')} required>
        {(field) => <TextInput autoComplete="off" maxLength={MAX_NAME_LENGTH} {...field} />}
      </FormField>
      <FormField control={control} name="role" label={t('users.fields.role')} required>
        {(field) => (
          <SelectInput
            {...field}
            options={[
              { value: 'FIRING_CHIEF', label: t('roles.FIRING_CHIEF') },
              { value: 'ADMIN', label: t('roles.ADMIN') },
            ]}
          />
        )}
      </FormField>
      <FormField control={control} name="locale" label={t('users.fields.locale')} required>
        {(field) => (
          <SelectInput
            {...field}
            options={SUPPORTED_LANGUAGES.map((language) => ({
              value: language,
              label: tCommon(`shell.language.options.${language}`),
              lang: language,
            }))}
          />
        )}
      </FormField>
    </>
  );
}
