import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useMemo } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router';
import {
  getGetComparsaQueryKey,
  getListComparsasQueryKey,
  useDeactivateComparsa,
  useDeleteComparsa,
  useGetComparsa,
  useReactivateComparsa,
  useUpdateComparsa,
} from '@/api/generated/comparsas/comparsas';
import {
  getListFiringChiefsQueryKey,
  useListFiringChiefs,
} from '@/api/generated/firing-chief-assignments/firing-chief-assignments';
import type { ComparsaResponse, FiringChiefResponse } from '@/api/generated/model';
import { ApiProblemError } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { ConfirmFailure } from '@/components/app/confirm-failure';
import { Form } from '@/components/app/FormField';
import { FormSection } from '@/components/app/FormSection';
import { PageHeader } from '@/components/app/PageHeader';
import { StatusBadge } from '@/components/app/StatusBadge';
import { useSession } from '@/features/identity-access/session';
import { NotFoundPage } from '@/features/platform/pages/NotFoundPage';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { FiringChiefsSection } from '../components/FiringChiefsSection';
import { noticeState, useNotice, type Announce } from '../notices';
import { applyFieldErrors, problemMessage } from '../problems';
import { ComparsaFields } from './ComparsaFields';
import {
  COMPARSA_CONFLICTS,
  COMPARSA_FIELDS,
  comparsaSchema,
  type ComparsaInput,
  type ComparsaValues,
} from './comparsaSchema';

/** Refreshes what a change to the comparsa affects: its page and the lists. */
function useRefreshAfterChange(id: string): () => Promise<void> {
  const queryClient = useQueryClient();
  return async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: getGetComparsaQueryKey(id) }),
      queryClient.invalidateQueries({ queryKey: getListComparsasQueryKey() }),
    ]);
  };
}

function EditForm({ comparsa, announce }: { comparsa: ComparsaResponse; announce: Announce }) {
  const { t } = useTranslation('catalog');
  const update = useUpdateComparsa();
  const refresh = useRefreshAfterChange(comparsa.id);
  const values = useMemo<ComparsaValues>(
    () => ({ name: comparsa.name, side: comparsa.side }),
    [comparsa.name, comparsa.side],
  );
  // Follows the server's values, but a refresh never discards what the Admin is typing.
  const form = useForm<ComparsaValues, unknown, ComparsaInput>({
    resolver: zodResolver(comparsaSchema),
    values,
    resetOptions: { keepDirtyValues: true },
  });

  const onSubmit = async (submitted: ComparsaInput): Promise<void> => {
    try {
      await update.mutateAsync({ id: comparsa.id, data: submitted });
    } catch (error) {
      if (!applyFieldErrors(error, COMPARSA_FIELDS, form.setError, COMPARSA_CONFLICTS)) {
        announce('error', problemMessage(t, error));
      }
      return;
    }
    form.reset(submitted);
    announce('success', t('comparsas.form.saved'));
    await refresh();
  };

  return (
    <Form form={form} onSubmit={onSubmit} className="max-w-xl">
      <ComparsaFields control={form.control} />
      <Button type="submit" pending={update.isPending}>
        {t('comparsas.form.save')}
      </Button>
    </Form>
  );
}

function Actions({
  comparsa,
  firingChiefs,
  announce,
}: {
  comparsa: ComparsaResponse;
  /** How many lose access, for the delete confirmation; undefined when unknown (not counted then). */
  firingChiefs: number | undefined;
  announce: Announce;
}) {
  const { t } = useTranslation('catalog');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const deactivate = useDeactivateComparsa();
  const reactivate = useReactivateComparsa();
  const remove = useDeleteComparsa();
  const refresh = useRefreshAfterChange(comparsa.id);

  const reactivateNow = async (): Promise<void> => {
    try {
      await reactivate.mutateAsync({ id: comparsa.id });
    } catch (error) {
      announce('error', problemMessage(t, error));
      return;
    }
    announce('success', t('comparsas.actions.reactivated'));
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

  const afterDeactivate = (): void => {
    announce('success', t('comparsas.actions.deactivated'));
    void refresh();
  };

  const afterDelete = (): void => {
    // Leave the page first, then drop the comparsa's queries: nothing refetches them into a 404.
    void Promise.resolve(navigate('/comparsas', { state: noticeState(t('comparsas.actions.deleted')) })).then(
      () => {
        queryClient.removeQueries({ queryKey: getGetComparsaQueryKey(comparsa.id) });
        queryClient.removeQueries({ queryKey: getListFiringChiefsQueryKey(comparsa.id) });
      },
    );
    void queryClient.invalidateQueries({ queryKey: getListComparsasQueryKey() });
  };

  const deleteDescription =
    firingChiefs === undefined || firingChiefs === 0
      ? t('comparsas.actions.deleteDescriptionNone')
      : t('comparsas.actions.deleteDescription', { count: firingChiefs });

  return (
    <FormSection title={t('comparsas.actions.title')}>
      <div className="flex flex-wrap gap-3">
        {comparsa.active ? (
          <ConfirmDialog
            title={t('comparsas.actions.deactivateTitle', { name: comparsa.name })}
            description={t('comparsas.actions.deactivateDescription')}
            confirmLabel={t('comparsas.actions.deactivate')}
            onConfirm={() => confirmed(() => deactivate.mutateAsync({ id: comparsa.id }))}
            onConfirmed={afterDeactivate}
            trigger={
              <Button type="button" variant="secondary">
                {t('comparsas.actions.deactivate')}
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
            {t('comparsas.actions.reactivate')}
          </Button>
        )}
        <ConfirmDialog
          title={t('comparsas.actions.deleteTitle', { name: comparsa.name })}
          description={deleteDescription}
          confirmLabel={t('comparsas.actions.delete')}
          onConfirm={() => confirmed(() => remove.mutateAsync({ id: comparsa.id }))}
          onConfirmed={afterDelete}
          trigger={
            <Button type="button" variant="destructive">
              {t('comparsas.actions.delete')}
            </Button>
          }
        />
      </div>
    </FormSection>
  );
}

/** Everything only an Admin may see or do; never mounted for a FiringChief (no Admin-only calls). */
function AdminPanel({ comparsa, announce }: { comparsa: ComparsaResponse; announce: Announce }) {
  const firingChiefs = useListFiringChiefs(comparsa.id);
  const chiefs = firingChiefs.data?.data as FiringChiefResponse[] | undefined;
  return (
    <>
      <EditForm comparsa={comparsa} announce={announce} />
      <div className="max-w-3xl min-w-0">
        <FiringChiefsSection
          comparsa={comparsa}
          chiefs={chiefs ?? []}
          isLoading={firingChiefs.isPending}
          error={firingChiefs.error ?? undefined}
        />
      </div>
      <Actions comparsa={comparsa} firingChiefs={chiefs?.length} announce={announce} />
    </>
  );
}

function ComparsaDetail({ id }: { id: string }) {
  const { t } = useTranslation('catalog');
  const session = useSession();
  const comparsa = useGetComparsa(id, { query: { retry: false } });
  const details = comparsa.data?.data as ComparsaResponse | undefined;
  useDocumentTitle(details?.name ?? t('comparsas.title'));
  const [notice, announce] = useNotice();
  const back = { to: '/comparsas', label: t('comparsas.detail.back') };

  if (comparsa.error instanceof ApiProblemError && comparsa.error.status === 404) {
    // Unknown and out of scope look the same (BR-12).
    return <NotFoundPage />;
  }
  if (comparsa.isError) {
    return (
      <>
        <PageHeader title={t('comparsas.title')} back={back} />
        <AlertBanner severity="error">{problemMessage(t, comparsa.error)}</AlertBanner>
      </>
    );
  }
  if (!details) {
    return <PageHeader title={t('comparsas.title')} back={back} />;
  }

  return (
    <>
      <PageHeader
        title={details.name}
        description={t(`side.${details.side}`)}
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
            {t('comparsas.detail.inactiveNotice')}
          </AlertBanner>
        )}
        {session.account?.role === 'ADMIN' && <AdminPanel comparsa={details} announce={announce} />}
      </div>
    </>
  );
}

/** Specs "Comparsa management by Admins" and "Comparsa visibility": one comparsa. */
export function ComparsaDetailPage() {
  const { id = '' } = useParams();
  return <ComparsaDetail key={id} id={id} />;
}
