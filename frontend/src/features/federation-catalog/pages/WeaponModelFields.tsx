import { useController, useWatch, type UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Handedness, Side, WeaponKind, WeaponSize } from '@/api/generated/model';
import { CheckboxField } from '@/components/app/CheckboxField';
import { FormField } from '@/components/app/FormField';
import { SelectInput } from '@/components/app/SelectInput';
import { TextInput } from '@/components/app/TextInput';
import { MAX_TEXT_LENGTH } from '../problems';
import type { WeaponModelInput, WeaponModelValues } from './weaponModelSchema';

type WeaponModelForm = UseFormReturn<WeaponModelValues, unknown, WeaponModelInput>;

/**
 * Label, kind, side, handedness, size and rentable. Choosing a pistol clears and disables
 * "rentable" and makes the attributes optional (BR-07); any other kind requires them.
 */
export function WeaponModelFields({ form }: { form: WeaponModelForm }) {
  const { t } = useTranslation('catalog');
  const { control } = form;
  const kind = useWatch({ control, name: 'kind' });
  const isPistol = kind === WeaponKind.PISTOL;
  const rentable = useController({ control, name: 'rentable' });
  const notSet = { value: '', label: t('weaponModels.form.notSet') };

  return (
    <>
      <FormField
        control={control}
        name="label"
        label={t('weaponModels.form.label')}
        description={t('weaponModels.form.labelHint')}
        required
      >
        {(field) => <TextInput autoComplete="off" maxLength={MAX_TEXT_LENGTH} {...field} />}
      </FormField>
      <FormField control={control} name="kind" label={t('weaponModels.form.kind')} required>
        {(field) => (
          <SelectInput
            {...field}
            onChange={(event) => {
              field.onChange(event);
              if (event.target.value === WeaponKind.PISTOL) {
                form.setValue('rentable', false, { shouldDirty: true });
                form.clearErrors(['side', 'handedness', 'size', 'rentable']);
              }
            }}
            options={[
              { value: '', label: t('validation.choice') },
              ...Object.values(WeaponKind).map((value) => ({ value, label: t(`kind.${value}`) })),
            ]}
          />
        )}
      </FormField>
      <FormField control={control} name="side" label={t('weaponModels.form.side')} required={!isPistol}>
        {(field) => (
          <SelectInput
            {...field}
            options={[notSet, ...Object.values(Side).map((value) => ({ value, label: t(`side.${value}`) }))]}
          />
        )}
      </FormField>
      <FormField
        control={control}
        name="handedness"
        label={t('weaponModels.form.handedness')}
        required={!isPistol}
      >
        {(field) => (
          <SelectInput
            {...field}
            options={[
              notSet,
              ...Object.values(Handedness).map((value) => ({ value, label: t(`handedness.${value}`) })),
            ]}
          />
        )}
      </FormField>
      <FormField control={control} name="size" label={t('weaponModels.form.size')} required={!isPistol}>
        {(field) => (
          <SelectInput
            {...field}
            options={[
              notSet,
              ...Object.values(WeaponSize).map((value) => ({ value, label: t(`size.${value}`) })),
            ]}
          />
        )}
      </FormField>
      <CheckboxField
        label={t('weaponModels.form.rentable')}
        description={isPistol ? t('weaponModels.form.pistolHint') : undefined}
        checked={rentable.field.value}
        onCheckedChange={rentable.field.onChange}
        disabled={isPistol}
      />
      {/* Choosing a pistol changes other fields: say so to screen-reader users (WCAG 4.1.3). */}
      <p role="status" className="sr-only">
        {isPistol ? t('weaponModels.form.pistolHint') : ''}
      </p>
    </>
  );
}
