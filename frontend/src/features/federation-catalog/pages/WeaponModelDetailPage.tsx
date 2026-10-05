import { zodResolver } from '@hookform/resolvers/zod';
import { useMemo, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import type { WeaponModelResponse } from '@/api/generated/model';
import {
  getGetWeaponModelQueryKey,
  getListWeaponModelsQueryKey,
  useDeactivateWeaponModel,
  useDeleteWeaponModel,
  useGetWeaponModel,
  useReactivateWeaponModel,
  useUpdateWeaponModel,
} from '@/api/generated/weapon-models/weapon-models';
import { ApiProblemError } from '@/api/http';
import { AlertBanner, NoticeBanner } from '@/components/app/AlertBanner';
import { DescriptionList } from '@/components/app/DescriptionList';
import { EditSheet, type EditResult } from '@/components/app/EditSheet';
import { PageHeader } from '@/components/app/PageHeader';
import { RecordHeader } from '@/components/app/RecordHeader';
import { SectionCard } from '@/components/app/SectionCard';
import { SectionGrid } from '@/components/app/SectionGrid';
import { StatusBadge } from '@/components/app/StatusBadge';
import { CategoryTag } from '@/components/app/Tag';
import { yesNo } from '@/components/app/tags';
import { useAppForm } from '@/components/app/use-app-form';
import { NotFoundPage } from '@/features/platform/pages/NotFoundPage';
import { useNotice, type Announce } from '@/lib/notices';
import { useInvalidate } from '@/lib/use-invalidate';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { useLifecycleActions } from '../components/useLifecycleActions';
import { applyFieldErrors, problemCode, problemMessage } from '../problems';
import { WeaponModelFields } from './WeaponModelFields';
import {
  WEAPON_MODEL_CONFLICTS,
  WEAPON_MODEL_FIELDS,
  weaponModelSchema,
  type WeaponModelInput,
  type WeaponModelValues,
} from './weaponModelSchema';

function useRefreshAfterChange(id: string): () => Promise<void> {
  return useInvalidate([getGetWeaponModelQueryKey(id), getListWeaponModelsQueryKey()]);
}

const valuesOf = (model: WeaponModelResponse): WeaponModelValues => ({
  kind: model.kind,
  side: model.side ?? '',
  handedness: model.handedness ?? '',
  size: model.size ?? '',
  rentable: model.rentable,
  label: model.label,
});

/** The model's attributes, read-only, with their edit panel (spec: Detail pages in read mode). */
function DataSection({ model }: { model: WeaponModelResponse }) {
  const { t } = useTranslation('catalog');
  const update = useUpdateWeaponModel();
  const refresh = useRefreshAfterChange(model.id);
  const values = useMemo(() => valuesOf(model), [model]);
  const form = useAppForm<WeaponModelValues, unknown, WeaponModelInput>({
    resolver: zodResolver(weaponModelSchema),
    defaultValues: values,
  });
  const notSet = t('weaponModels.form.notSet');

  // The weapon model API has no version: the last save wins (design D9).
  const save = async (submitted: WeaponModelInput): Promise<EditResult> => {
    try {
      await update.mutateAsync({ id: model.id, data: submitted });
    } catch (error) {
      if (problemCode(error) === 'weaponModels.notFound') {
        await refresh();
        return { status: 'rejected', reason: problemMessage(t, error) };
      }
      return applyFieldErrors(error, WEAPON_MODEL_FIELDS, form.setError, WEAPON_MODEL_CONFLICTS)
        ? { status: 'kept' }
        : { status: 'rejected', reason: problemMessage(t, error) };
    }
    await refresh();
    return { status: 'saved' };
  };

  return (
    <SectionCard
      title={t('weaponModels.form.section')}
      action={
        <EditSheet
          title={t('weaponModels.detail.data.edit')}
          sectionName={t('weaponModels.detail.data.name')}
          form={form}
          values={values}
          onSave={save}
        >
          <WeaponModelFields form={form} />
        </EditSheet>
      }
    >
      <DescriptionList
        items={[
          { term: t('weaponModels.form.label'), value: model.label },
          {
            term: t('weaponModels.form.kind'),
            value: <CategoryTag category="weaponKind" value={model.kind} />,
          },
          {
            term: t('weaponModels.form.side'),
            value: model.side ? <CategoryTag category="side" value={model.side} /> : notSet,
          },
          {
            term: t('weaponModels.form.handedness'),
            value: model.handedness ? t(`handedness.${model.handedness}`) : notSet,
          },
          { term: t('weaponModels.form.size'), value: model.size ? t(`size.${model.size}`) : notSet },
          {
            term: t('weaponModels.form.rentable'),
            value: <CategoryTag category="yesNo" value={yesNo(model.rentable)} />,
          },
        ]}
      />
    </SectionCard>
  );
}

/** Deactivate, reactivate and delete, from "More actions" (spec: Action hierarchy). */
function useModelActions(model: WeaponModelResponse, announce: Announce) {
  const { t } = useTranslation('catalog');
  const { mutateAsync: deactivate } = useDeactivateWeaponModel();
  const { mutateAsync: reactivate } = useReactivateWeaponModel();
  const { mutateAsync: remove } = useDeleteWeaponModel();
  const id = model.id;
  return useLifecycleActions({
    active: model.active,
    texts: {
      deactivate: t('weaponModels.actions.deactivate'),
      deactivateTitle: t('weaponModels.actions.deactivateTitle', { label: model.label }),
      deactivateDescription: t('weaponModels.actions.deactivateDescription'),
      deactivated: t('weaponModels.actions.deactivated'),
      reactivate: t('weaponModels.actions.reactivate'),
      reactivated: t('weaponModels.actions.reactivated'),
      delete: t('weaponModels.actions.delete'),
      deleteTitle: t('weaponModels.actions.deleteTitle', { label: model.label }),
      deleteDescription: t('weaponModels.actions.deleteDescription'),
      deleted: t('weaponModels.actions.deleted'),
    },
    deactivate: () => deactivate({ id }),
    reactivate: () => reactivate({ id }),
    remove: () => remove({ id }),
    refresh: useRefreshAfterChange(id),
    announce,
    explain: (error) => problemMessage(t, error),
    listPath: '/weapon-models',
    listKey: getListWeaponModelsQueryKey(),
    recordKeys: [getGetWeaponModelQueryKey(id)],
  });
}

function ModelDetail({
  model,
  announce,
  notice,
}: {
  model: WeaponModelResponse;
  announce: Announce;
  notice: ReactNode;
}) {
  const { t } = useTranslation('catalog');
  const actions = useModelActions(model, announce);
  return (
    <>
      <RecordHeader
        back={{ to: '/weapon-models', label: t('weaponModels.detail.back') }}
        context={<CategoryTag category="weaponKind" value={model.kind} />}
        name={model.label}
        statuses={<StatusBadge kind="catalog" value={model.active ? 'ACTIVE' : 'INACTIVE'} />}
        moreActions={actions.items}
        moreActionsRef={actions.moreActions}
      />
      {notice}
      {!model.active && (
        <AlertBanner severity="info" className="max-w-form" live={false}>
          {t('weaponModels.detail.inactiveNotice')}
        </AlertBanner>
      )}
      <SectionGrid>
        <DataSection model={model} />
      </SectionGrid>
      {actions.dialogs}
    </>
  );
}

function WeaponModelDetail({ id }: { id: string }) {
  const { t } = useTranslation('catalog');
  const model = useGetWeaponModel(id, { query: { retry: false } });
  const details = model.data?.data as WeaponModelResponse | undefined;
  useDocumentTitle(details?.label ?? t('weaponModels.title'));
  const [notice, announce] = useNotice();
  const back = { to: '/weapon-models', label: t('weaponModels.detail.back') };

  if (model.error instanceof ApiProblemError && model.error.status === 404) {
    return <NotFoundPage />;
  }
  if (model.isError && !details) {
    return (
      <>
        <PageHeader title={t('weaponModels.title')} back={back} />
        <AlertBanner severity="error">{problemMessage(t, model.error)}</AlertBanner>
      </>
    );
  }
  if (!details) {
    return <PageHeader title={t('weaponModels.title')} back={back} />;
  }

  return <ModelDetail model={details} announce={announce} notice={<NoticeBanner notice={notice} />} />;
}

/** Spec "Weapon models (BR-07)": one model in read mode; edit, deactivate or delete it (Admins only). */
export function WeaponModelDetailPage() {
  const { id = '' } = useParams();
  return <WeaponModelDetail key={id} id={id} />;
}
