import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useMemo } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router';
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
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { ConfirmFailure } from '@/components/app/confirm-failure';
import { Form } from '@/components/app/FormField';
import { FormSection } from '@/components/app/FormSection';
import { PageHeader } from '@/components/app/PageHeader';
import { StatusBadge } from '@/components/app/StatusBadge';
import { NotFoundPage } from '@/features/platform/pages/NotFoundPage';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { noticeState, useNotice, type Announce } from '@/lib/notices';
import { applyFieldErrors, problemMessage } from '../problems';
import { WeaponModelFields } from './WeaponModelFields';
import {
  WEAPON_MODEL_CONFLICTS,
  WEAPON_MODEL_FIELDS,
  weaponModelSchema,
  type WeaponModelInput,
  type WeaponModelValues,
} from './weaponModelSchema';

function useRefreshAfterChange(id: string): () => Promise<void> {
  const queryClient = useQueryClient();
  return async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: getGetWeaponModelQueryKey(id) }),
      queryClient.invalidateQueries({ queryKey: getListWeaponModelsQueryKey() }),
    ]);
  };
}

function EditForm({ model, announce }: { model: WeaponModelResponse; announce: Announce }) {
  const { t } = useTranslation('catalog');
  const update = useUpdateWeaponModel();
  const refresh = useRefreshAfterChange(model.id);
  const values = useMemo<WeaponModelValues>(
    () => ({
      kind: model.kind,
      side: model.side ?? '',
      handedness: model.handedness ?? '',
      size: model.size ?? '',
      // A pistol is never rentable (BR-07); never start its form in a state it cannot save.
      rentable: model.kind === 'PISTOL' ? false : model.rentable,
      label: model.label,
    }),
    [model.kind, model.side, model.handedness, model.size, model.rentable, model.label],
  );
  // Follows the server's values, but a refresh never discards what the Admin is typing.
  const form = useForm<WeaponModelValues, unknown, WeaponModelInput>({
    resolver: zodResolver(weaponModelSchema),
    values,
    resetOptions: { keepDirtyValues: true },
  });

  const onSubmit = async (submitted: WeaponModelInput): Promise<void> => {
    // What was saved: typing during the request must not become the new baseline.
    const saved = form.getValues();
    try {
      await update.mutateAsync({ id: model.id, data: submitted });
    } catch (error) {
      if (!applyFieldErrors(error, WEAPON_MODEL_FIELDS, form.setError, WEAPON_MODEL_CONFLICTS)) {
        announce('error', problemMessage(t, error));
      }
      return;
    }
    form.reset(saved);
    announce('success', t('weaponModels.form.saved'));
    await refresh();
  };

  return (
    <Form form={form} onSubmit={onSubmit} className="max-w-xl">
      <WeaponModelFields form={form} />
      <Button type="submit" pending={update.isPending}>
        {t('weaponModels.form.save')}
      </Button>
    </Form>
  );
}

function Actions({ model, announce }: { model: WeaponModelResponse; announce: Announce }) {
  const { t } = useTranslation('catalog');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const deactivate = useDeactivateWeaponModel();
  const reactivate = useReactivateWeaponModel();
  const remove = useDeleteWeaponModel();
  const refresh = useRefreshAfterChange(model.id);

  const reactivateNow = async (): Promise<void> => {
    try {
      await reactivate.mutateAsync({ id: model.id });
    } catch (error) {
      announce('error', problemMessage(t, error));
      return;
    }
    announce('success', t('weaponModels.actions.reactivated'));
    await refresh();
  };

  /** A confirmed action: a failure stays in the dialog with its reason; the outcome follows once it closes. */
  const confirmed = async (action: () => Promise<unknown>): Promise<void> => {
    try {
      await action();
    } catch (error) {
      throw new ConfirmFailure(problemMessage(t, error));
    }
  };

  const afterDelete = (): void => {
    // Leave the page first, then drop the model's query: nothing refetches it into a 404.
    void Promise.resolve(
      navigate('/weapon-models', { state: noticeState(t('weaponModels.actions.deleted')) }),
    ).then(() => {
      queryClient.removeQueries({ queryKey: getGetWeaponModelQueryKey(model.id) });
    });
    void queryClient.invalidateQueries({ queryKey: getListWeaponModelsQueryKey() });
  };

  return (
    <FormSection title={t('weaponModels.actions.title')}>
      <div className="flex flex-wrap gap-3">
        {model.active ? (
          <ConfirmDialog
            title={t('weaponModels.actions.deactivateTitle', { label: model.label })}
            description={t('weaponModels.actions.deactivateDescription')}
            confirmLabel={t('weaponModels.actions.deactivate')}
            onConfirm={() => confirmed(() => deactivate.mutateAsync({ id: model.id }))}
            onConfirmed={() => {
              announce('success', t('weaponModels.actions.deactivated'));
              void refresh();
            }}
            trigger={
              <Button type="button" variant="secondary">
                {t('weaponModels.actions.deactivate')}
              </Button>
            }
          />
        ) : (
          <Button
            type="button"
            variant="secondary"
            pending={reactivate.isPending}
            onClick={() => void reactivateNow()}
          >
            {t('weaponModels.actions.reactivate')}
          </Button>
        )}
        <ConfirmDialog
          title={t('weaponModels.actions.deleteTitle', { label: model.label })}
          description={t('weaponModels.actions.deleteDescription')}
          confirmLabel={t('weaponModels.actions.delete')}
          onConfirm={() => confirmed(() => remove.mutateAsync({ id: model.id }))}
          onConfirmed={afterDelete}
          trigger={
            <Button type="button" variant="destructive">
              {t('weaponModels.actions.delete')}
            </Button>
          }
        />
      </div>
    </FormSection>
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
  if (model.isError) {
    return (
      <>
        <PageHeader title={t('weaponModels.form.editTitle')} back={back} />
        <AlertBanner severity="error">{problemMessage(t, model.error)}</AlertBanner>
      </>
    );
  }
  if (!details) {
    return <PageHeader title={t('weaponModels.form.editTitle')} back={back} />;
  }

  return (
    <>
      <PageHeader
        title={details.label}
        description={t(`kind.${details.kind}`)}
        back={back}
        actions={<StatusBadge kind="catalog" value={details.active ? 'ACTIVE' : 'INACTIVE'} />}
      />
      <div className="grid gap-10">
        {notice && (
          <AlertBanner key={notice.id} severity={notice.severity} className="max-w-xl" focusOnMount>
            {notice.text}
          </AlertBanner>
        )}
        {!details.active && (
          <AlertBanner severity="info" className="max-w-xl">
            {t('weaponModels.detail.inactiveNotice')}
          </AlertBanner>
        )}
        <EditForm model={details} announce={announce} />
        <Actions model={details} announce={announce} />
      </div>
    </>
  );
}

/** Spec "Weapon models": edit, deactivate or delete one model (Admins only). */
export function WeaponModelDetailPage() {
  const { id = '' } = useParams();
  return <WeaponModelDetail key={id} id={id} />;
}
