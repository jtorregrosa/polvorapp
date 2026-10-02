import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router';
import type { WeaponModelResponse } from '@/api/generated/model';
import {
  getListWeaponModelsQueryKey,
  useCreateWeaponModel,
} from '@/api/generated/weapon-models/weapon-models';
import { responseData } from '@/api/http';
import { ActionBar } from '@/components/app/ActionBar';
import { Button } from '@/components/app/Button';
import { Form } from '@/components/app/FormField';
import { FormLayout } from '@/components/app/FormLayout';
import { PageHeader } from '@/components/app/PageHeader';
import { useAppForm } from '@/components/app/use-app-form';
import { noticeState } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
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

/** Spec "Weapon models": an Admin adds a model to the catalogue (form template). */
export function WeaponModelFormPage() {
  const { t } = useTranslation('catalog');
  useDocumentTitle(t('weaponModels.form.newTitle'));
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const create = useCreateWeaponModel();
  const form = useAppForm<WeaponModelValues, unknown, WeaponModelInput>({
    resolver: zodResolver(weaponModelSchema),
    defaultValues: EMPTY_WEAPON_MODEL,
  });

  const onSubmit = async (values: WeaponModelInput): Promise<void> => {
    let created: WeaponModelResponse;
    try {
      created = responseData(await create.mutateAsync({ data: values }));
    } catch (error) {
      if (!applyFieldErrors(error, WEAPON_MODEL_FIELDS, form.setError, WEAPON_MODEL_CONFLICTS)) {
        // A refusal about no field (e.g. a duplicate combination) is listed in the error summary.
        form.setError('root.server', { type: 'server', message: problemMessage(t, error) });
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
      <Form form={form} onSubmit={onSubmit}>
        <FormLayout
          sections={[
            {
              id: 'model',
              title: t('weaponModels.form.section'),
              content: <WeaponModelFields form={form} />,
            },
          ]}
          actions={
            <ActionBar
              secondary={
                <Button asChild variant="secondary">
                  <Link to="/weapon-models">{t('weaponModels.form.cancel')}</Link>
                </Button>
              }
              primary={
                <Button type="submit" pending={create.isPending}>
                  {t('weaponModels.form.create')}
                </Button>
              }
            />
          }
        />
      </Form>
    </>
  );
}
