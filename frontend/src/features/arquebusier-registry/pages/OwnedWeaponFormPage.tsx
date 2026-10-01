import { zodResolver } from '@hookform/resolvers/zod';
import { useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
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
import { Button } from '@/components/app/Button';
import { Form, FormField } from '@/components/app/FormField';
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
  const [failure, setFailure] = useState<unknown>();
  // Without includeInactive the catalogue lists only active models; a weapon keeps its retired one.
  const models = useListWeaponModels();
  const options = useMemo(() => {
    const active = (models.data?.data ?? []) as WeaponModelResponse[];
    const current = weapon && !active.some((model) => model.id === weapon.model.id) ? [weapon.model] : [];
    return [...active, ...current].map((model) => ({ value: model.id, label: modelLabel(model) }));
  }, [modelLabel, models.data, weapon]);
  const values = useMemo(() => (weapon ? ownedWeaponValuesOf(weapon) : EMPTY_OWNED_WEAPON), [weapon]);
  // Follows the server's values (after a conflict too), but never discards what the user typed.
  const form = useForm<OwnedWeaponValues>({
    resolver: zodResolver(ownedWeaponSchema),
    values,
    resetOptions: { keepDirtyValues: true },
  });
  const back = `/arquebusiers/${arquebusier.id}`;

  const onSubmit = async (submitted: OwnedWeaponValues): Promise<void> => {
    setFailure(undefined);
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
        // Removed meanwhile: the refreshed page says it is not found.
        await refresh();
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
      if (!placed) setFailure(error);
      return;
    }
    await refresh();
    await navigate(back, {
      state: noticeState(t(weapon ? 'ownedWeapons.form.saved' : 'ownedWeapons.form.added')),
    });
  };

  return (
    <Form form={form} onSubmit={onSubmit} className="max-w-xl">
      <FormField
        control={form.control}
        name="weaponModelId"
        label={t('ownedWeapons.form.model')}
        description={t('ownedWeapons.form.modelHint')}
        required
      >
        {(field) => (
          <SelectInput {...field} options={[{ value: '', label: t('validation.choice') }, ...options]} />
        )}
      </FormField>
      {models.isError && (
        <LoadFailure error={models.error} consequence={t('load.models')} onRetry={() => models.refetch()} />
      )}
      <FormField
        control={form.control}
        name="weaponNumber"
        label={t('ownedWeapons.form.weaponNumber')}
        description={t('ownedWeapons.form.weaponNumberHint')}
        required
      >
        {(field) => <TextInput autoComplete="off" maxLength={MAX_NUMBER_LENGTH} {...field} />}
      </FormField>
      <FormField
        control={form.control}
        name="ownershipGuideNumber"
        label={t('ownedWeapons.form.guide')}
        required
      >
        {(field) => (
          <TextInput
            autoComplete="off"
            maxLength={MAX_NUMBER_LENGTH}
            autoCapitalize="characters"
            {...field}
          />
        )}
      </FormField>
      {failure !== undefined && <AlertBanner severity="error">{problemMessage(t, failure)}</AlertBanner>}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" pending={add.isPending || update.isPending}>
          {t(weapon ? 'ownedWeapons.form.save' : 'ownedWeapons.form.add')}
        </Button>
        <Button asChild variant="secondary">
          <Link to={back}>{t('ownedWeapons.form.cancel')}</Link>
        </Button>
      </div>
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
          className="mb-4 max-w-xl"
          error={arquebusier.error}
          consequence={details ? t('load.stale') : undefined}
          onRetry={() => arquebusier.refetch()}
        />
      )}
      {details && !details.comparsaActive && (
        <AlertBanner severity="info" className="mb-4 max-w-xl">
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
