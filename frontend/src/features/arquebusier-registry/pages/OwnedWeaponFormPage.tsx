import { zodResolver } from '@hookform/resolvers/zod';
import { useMemo } from 'react';
import { useAppForm } from '@/components/app/use-app-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router';
import {
  useAddOwnedWeapon,
  useGetArquebusier,
  useUpdateOwnedWeapon,
} from '@/api/generated/arquebusiers/arquebusiers';
import type { ArquebusierResponse, OwnedWeaponResponse, WeaponModelResponse } from '@/api/generated/model';
import { useListWeaponModels } from '@/api/generated/weapon-models/weapon-models';
import { ApiProblemError } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { ActionBar } from '@/components/app/ActionBar';
import { Button } from '@/components/app/Button';
import { Form, FormField } from '@/components/app/FormField';
import { FormLayout } from '@/components/app/FormLayout';
import { PageHeader } from '@/components/app/PageHeader';
import { SelectInput } from '@/components/app/SelectInput';
import { TextInput } from '@/components/app/TextInput';
import { NotFoundPage } from '@/features/platform/pages/NotFoundPage';
import { noticeState } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { LoadFailure } from '../components/LoadFailure';
import { useModelLabel, useRefreshArquebusier } from '../hooks';
import {
  EMPTY_OWNED_WEAPON,
  MAX_NUMBER_LENGTH,
  OWNED_WEAPON_FIELDS,
  ownedWeaponSchema,
  ownedWeaponValuesOf,
  type OwnedWeaponValues,
} from '../ownedWeaponSchema';
import { applyFieldErrors, problemCode, problemMessage } from '../problems';

function WeaponForm({
  arquebusier,
  weapon,
}: {
  arquebusier: ArquebusierResponse;
  weapon?: OwnedWeaponResponse;
}) {
  const { t } = useTranslation('registry');
  const navigate = useNavigate();
  const modelLabel = useModelLabel();
  const refresh = useRefreshArquebusier(arquebusier.id);
  const add = useAddOwnedWeapon();
  const update = useUpdateOwnedWeapon();
  // Without includeInactive the catalogue lists only active models; a weapon keeps its retired one.
  const models = useListWeaponModels();
  const options = useMemo(() => {
    const active = (models.data?.data ?? []) as WeaponModelResponse[];
    const current = weapon && !active.some((model) => model.id === weapon.model.id) ? [weapon.model] : [];
    return [...active, ...current].map((model) => ({ value: model.id, label: modelLabel(model) }));
  }, [modelLabel, models.data, weapon]);
  const values = useMemo(() => (weapon ? ownedWeaponValuesOf(weapon) : EMPTY_OWNED_WEAPON), [weapon]);
  // Follows the server's values (after a conflict too), but never discards what the user typed.
  const form = useAppForm<OwnedWeaponValues>({
    resolver: zodResolver(ownedWeaponSchema),
    values,
    resetOptions: { keepDirtyValues: true, keepErrors: true },
  });
  const back = `/arquebusiers/${arquebusier.id}`;

  const onSubmit = async (submitted: OwnedWeaponValues): Promise<void> => {
    const data = {
      weaponModelId: submitted.weaponModelId,
      weaponNumber: submitted.weaponNumber.trim(),
      ownershipGuideNumber: submitted.ownershipGuideNumber.trim(),
    };
    try {
      if (weapon) {
        await update.mutateAsync({
          id: arquebusier.id,
          weaponId: weapon.id,
          data: { ...data, version: weapon.version },
        });
      } else {
        await add.mutateAsync({ id: arquebusier.id, data });
      }
    } catch (error) {
      const code = problemCode(error);
      if (code === 'ownedWeapons.notFound' || code === 'arquebusiers.notFound') {
        // Removed meanwhile: the refreshed page says it is not found; should the refresh fail, the
        // form still says why nothing was saved.
        await refresh();
        form.setError('root.server', { type: 'server', message: problemMessage(t, error) });
        return;
      }
      if (code === 'ownedWeapons.modified') {
        await refresh();
      }
      const placed = applyFieldErrors(error, OWNED_WEAPON_FIELDS, form.setError, {
        conflicts: {
          'ownedWeapons.guideTaken': 'ownershipGuideNumber',
          'ownedWeapons.modelInactive': 'weaponModelId',
        },
      });
      // A refusal about no field is listed in the error summary, which takes focus.
      if (!placed) form.setError('root.server', { type: 'server', message: problemMessage(t, error) });
      return;
    }
    await refresh();
    await navigate(back, {
      state: noticeState(t(weapon ? 'ownedWeapons.form.saved' : 'ownedWeapons.form.added')),
    });
  };

  const fields = (
    <>
      <FormField
        control={form.control}
        name="weaponModelId"
        label={t('ownedWeapons.form.model')}
        description={t('ownedWeapons.form.modelHint')}
        width="name"
      >
        {(field) => <SelectInput {...field} placeholder={t('validation.choice')} options={options} />}
      </FormField>
      {models.isError && (
        <LoadFailure error={models.error} consequence={t('load.models')} onRetry={() => models.refetch()} />
      )}
      <FormField
        control={form.control}
        name="weaponNumber"
        label={t('ownedWeapons.form.weaponNumber')}
        description={t('ownedWeapons.form.weaponNumberHint')}
        width="id"
      >
        {(field) => (
          <TextInput autoComplete="off" maxLength={MAX_NUMBER_LENGTH} className="font-mono" {...field} />
        )}
      </FormField>
      <FormField
        control={form.control}
        name="ownershipGuideNumber"
        label={t('ownedWeapons.form.guide')}
        width="id"
      >
        {(field) => (
          <TextInput
            autoComplete="off"
            maxLength={MAX_NUMBER_LENGTH}
            autoCapitalize="characters"
            className="font-mono"
            {...field}
          />
        )}
      </FormField>
    </>
  );

  return (
    <Form form={form} onSubmit={onSubmit}>
      <FormLayout
        sections={[{ id: 'weapon', title: t('ownedWeapons.form.section'), content: fields }]}
        actions={
          <ActionBar
            secondary={
              <Button asChild variant="secondary">
                <Link to={back}>{t('ownedWeapons.form.cancel')}</Link>
              </Button>
            }
            primary={
              <Button type="submit" pending={add.isPending || update.isPending}>
                {t(weapon ? 'ownedWeapons.form.save' : 'ownedWeapons.form.add')}
              </Button>
            }
          />
        }
      />
    </Form>
  );
}

function OwnedWeaponForm({ id, weaponId }: { id: string; weaponId: string | undefined }) {
  const { t } = useTranslation('registry');
  const title = t(weaponId ? 'ownedWeapons.form.editTitle' : 'ownedWeapons.form.newTitle');
  useDocumentTitle(title);
  const arquebusier = useGetArquebusier(id, { query: { retry: false } });
  const details = arquebusier.data?.data as ArquebusierResponse | undefined;
  const weapon = weaponId ? details?.ownedWeapons.find((owned) => owned.id === weaponId) : undefined;
  const back = { to: `/arquebusiers/${id}`, label: t('ownedWeapons.form.back') };

  if (
    (arquebusier.error instanceof ApiProblemError && arquebusier.error.status === 404) ||
    (details && weaponId && !weapon)
  ) {
    // Unknown and out of scope look the same (BR-12).
    return <NotFoundPage />;
  }

  return (
    <>
      <PageHeader
        title={title}
        description={
          details
            ? `${details.firstName} ${details.lastName} · ${t('ownedWeapons.form.description')}`
            : undefined
        }
        back={back}
      />
      {arquebusier.isError && (
        <LoadFailure
          className="max-w-form"
          error={arquebusier.error}
          consequence={details ? t('load.stale') : undefined}
          onRetry={() => arquebusier.refetch()}
        />
      )}
      {details && !details.comparsaActive && (
        <AlertBanner severity="info" className="max-w-form" live={false}>
          {t('detail.inactiveComparsa', { name: details.comparsaName })}
        </AlertBanner>
      )}
      {details && <WeaponForm key={weaponId ?? 'new'} arquebusier={details} weapon={weapon} />}
    </>
  );
}

/** Spec "Owned weapons (UC-04)": adds an owned weapon, or edits one (`weaponId`), of an arquebusier in scope. */
export function OwnedWeaponFormPage() {
  const { id = '', weaponId } = useParams();
  return <OwnedWeaponForm key={`${id}/${weaponId ?? 'new'}`} id={id} weaponId={weaponId} />;
}
