import { useController, useWatch, type UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Handedness, Side, WeaponKind, WeaponSize } from '@/api/generated/model';
import { CheckboxField } from '@/components/app/CheckboxField';
import { FormField } from '@/components/app/FormField';
import { RadioCards } from '@/components/app/RadioCards';
import { TextInput } from '@/components/app/TextInput';
import { MAX_TEXT_LENGTH } from '../problems';
import type { WeaponModelInput, WeaponModelValues } from './weaponModelSchema';

type WeaponModelForm = UseFormReturn<WeaponModelValues, unknown, WeaponModelInput>;
type Attribute = 'side' | 'handedness' | 'size';

/** Radio cards cannot hold an empty value: a pistol's "not set" attribute is this option. */
const NOT_SET = 'NOT_SET';

/** Side, handedness or size as radio cards: required, or optional with "not set" for a pistol. */
function AttributeField({
  control,
  name,
  optional,
}: {
  control: WeaponModelForm['control'];
  name: Attribute;
  optional: boolean;
}) {
  const { t } = useTranslation('catalog');
  const options =
    name === 'side'
      ? Object.values(Side).map((value) => ({ value, label: t(`side.${value}`) }))
      : name === 'handedness'
        ? Object.values(Handedness).map((value) => ({ value, label: t(`handedness.${value}`) }))
        : Object.values(WeaponSize).map((value) => ({ value, label: t(`size.${value}`) }));
  return (
    <FormField control={control} name={name} label={t(`weaponModels.form.${name}`)} optional={optional}>
      {(field) => (
        <RadioCards
          {...field}
          value={optional && field.value === '' ? NOT_SET : field.value}
          onChange={(value) => {
            field.onChange(value === NOT_SET ? '' : value);
          }}
          options={
            optional ? [...options, { value: NOT_SET, label: t('weaponModels.form.notSet') }] : options
          }
        />
      )}
    </FormField>
  );
}

/** What a chosen kind adds under it (spec: Weapon models, BR-07). */
function KindDetails({ form, isPistol }: { form: WeaponModelForm; isPistol: boolean }) {
  const { t } = useTranslation('catalog');
  const rentable = useController({ control: form.control, name: 'rentable' });
  return (
    <div className="flex flex-col gap-group">
      {/* Read once, from the status below: hidden here from assistive technology. */}
      {isPistol && (
        <p aria-hidden="true" className="text-help text-muted-foreground">
          {t('weaponModels.form.pistolHint')}
        </p>
      )}
      <AttributeField control={form.control} name="side" optional={isPistol} />
      <AttributeField control={form.control} name="handedness" optional={isPistol} />
      <AttributeField control={form.control} name="size" optional={isPistol} />
      {!isPistol && (
        <CheckboxField
          label={t('weaponModels.form.rentable')}
          checked={rentable.field.value}
          onCheckedChange={rentable.field.onChange}
        />
      )}
    </div>
  );
}

/**
 * Label, then the kind as radio cards; the chosen kind reveals side, handedness, size and, except
 * for a pistol, rentable. Choosing a pistol clears "rentable" and makes the attributes optional
 * (BR-07); any other kind requires them.
 */
export function WeaponModelFields({ form }: { form: WeaponModelForm }) {
  const { t } = useTranslation('catalog');
  const { control } = form;
  const kind = useWatch({ control, name: 'kind' });
  const isPistol = kind === WeaponKind.PISTOL;
  const details = <KindDetails form={form} isPistol={isPistol} />;

  return (
    <>
      <FormField
        control={control}
        name="label"
        label={t('weaponModels.form.label')}
        description={t('weaponModels.form.labelHint')}
        width="name"
      >
        {(field) => <TextInput autoComplete="off" maxLength={MAX_TEXT_LENGTH} {...field} />}
      </FormField>
      <FormField control={control} name="kind" label={t('weaponModels.form.kind')}>
        {(field) => (
          <RadioCards
            {...field}
            onChange={(value) => {
              field.onChange(value);
              if (value === WeaponKind.PISTOL) {
                form.setValue('rentable', false, { shouldDirty: true });
                form.clearErrors(['side', 'handedness', 'size', 'rentable']);
              }
            }}
            options={Object.values(WeaponKind).map((value) => ({
              value,
              label: t(`kind.${value}`),
              reveal: details,
            }))}
          />
        )}
      </FormField>
      {/* Choosing a kind shows other fields under it: say so to screen-reader users (WCAG 4.1.3). */}
      <p role="status" className="sr-only">
        {kind === ''
          ? ''
          : isPistol
            ? t('weaponModels.form.pistolHint')
            : t('weaponModels.form.attributesShown')}
      </p>
    </>
  );
}
