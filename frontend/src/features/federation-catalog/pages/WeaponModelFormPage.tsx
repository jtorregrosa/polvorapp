import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import type { WeaponModelResponse } from '@/api/generated/model';
import {
  getListWeaponModelsQueryKey,
  useCreateWeaponModel,
} from '@/api/generated/weapon-models/weapon-models';
import { responseData } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { Form } from '@/components/app/FormField';
import { PageHeader } from '@/components/app/PageHeader';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { noticeState } from '../notices';
import { applyFieldErrors, problemMessage } from '../problems';
import { WeaponModelFields } from './WeaponModelFields';
import {
  EMPTY_WEAPON_MODEL,
  WEAPON_MODEL_CONFLICTS,
  WEAPON_MODEL_FIELDS,
  weaponModelSchema,
  type WeaponModelInput,
  type WeaponModelValues,
} from './weaponModelSchema';

/** Spec "Weapon models": an Admin adds a model to the catalogue. */
export function WeaponModelFormPage() {
  const { t } = useTranslation('catalog');
  useDocumentTitle(t('weaponModels.form.newTitle'));
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const create = useCreateWeaponModel();
  const [failure, setFailure] = useState<unknown>();
  const form = useForm<WeaponModelValues, unknown, WeaponModelInput>({
    resolver: zodResolver(weaponModelSchema),
    defaultValues: EMPTY_WEAPON_MODEL,
  });

  const onSubmit = async (values: WeaponModelInput): Promise<void> => {
    let created: WeaponModelResponse;
    setFailure(undefined);
    try {
      created = responseData(await create.mutateAsync({ data: values }));
    } catch (error) {
      if (!applyFieldErrors(error, WEAPON_MODEL_FIELDS, form.setError, WEAPON_MODEL_CONFLICTS)) {
        setFailure(error);
      }
      return;
    }
    void queryClient.invalidateQueries({ queryKey: getListWeaponModelsQueryKey() });
    await navigate(`/weapon-models/${created.id}`, { state: noticeState(t('weaponModels.form.created')) });
  };

  return (
    <>
      <PageHeader
        title={t('weaponModels.form.newTitle')}
        description={t('weaponModels.form.newDescription')}
        back={{ to: '/weapon-models', label: t('weaponModels.detail.back') }}
      />
      <Form form={form} onSubmit={onSubmit} className="max-w-xl">
        <WeaponModelFields form={form} />
        {failure !== undefined && <AlertBanner severity="error">{problemMessage(t, failure)}</AlertBanner>}
        <Button type="submit" pending={create.isPending}>
          {t('weaponModels.form.create')}
        </Button>
      </Form>
    </>
  );
}
