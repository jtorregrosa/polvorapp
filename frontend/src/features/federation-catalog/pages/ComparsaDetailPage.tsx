import { zodResolver } from '@hookform/resolvers/zod';
import { useMemo, type ReactNode, type Ref } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
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
import { useListArquebusiers } from '@/api/generated/arquebusiers/arquebusiers';
import type { ArquebusierRowResponse, ComparsaResponse, FiringChiefResponse } from '@/api/generated/model';
import { ApiProblemError } from '@/api/http';
import { AlertBanner, NoticeBanner } from '@/components/app/AlertBanner';
import { ComparsaLogo } from '@/components/app/ComparsaLogo';
import { DescriptionList } from '@/components/app/DescriptionList';
import { EditSheet, type EditResult } from '@/components/app/EditSheet';
import { PageHeader } from '@/components/app/PageHeader';
import { RecordHeader, type MoreAction } from '@/components/app/RecordHeader';
import { SectionCard } from '@/components/app/SectionCard';
import { SectionGrid } from '@/components/app/SectionGrid';
import { StatusBadge } from '@/components/app/StatusBadge';
import { CategoryTag } from '@/components/app/Tag';
import { useAppForm } from '@/components/app/use-app-form';
import { BadgeSheet } from '@/features/badges/components/BadgeSheet';
import { useSession } from '@/features/identity-access/session';
import { NotFoundPage } from '@/features/platform/pages/NotFoundPage';
import { useNotice, type Announce } from '@/lib/notices';
import { useInvalidate } from '@/lib/use-invalidate';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { ComparsaLogoSection } from '../components/ComparsaLogoSection';
import { FiringChiefsSection } from '../components/FiringChiefsSection';
import { useLifecycleActions } from '../components/useLifecycleActions';
import { logoUrl } from '../logos';
import { applyFieldErrors, problemCode, problemMessage } from '../problems';
import { ComparsaFields } from './ComparsaFields';
import {
  COMPARSA_CONFLICTS,
  COMPARSA_FIELDS,
  comparsaSchema,
  type ComparsaInput,
  type ComparsaValues,
} from './comparsaSchema';

const NO_CHIEFS: FiringChiefResponse[] = [];

/** Refreshes what a change to the comparsa affects: its page and the lists. */
function useRefreshAfterChange(id: string): () => Promise<void> {
  return useInvalidate([getGetComparsaQueryKey(id), getListComparsasQueryKey()]);
}

/** Name and side, read-only, with their edit panel for an Admin (spec: Detail pages in read mode). */
function DataSection({ comparsa, isAdmin }: { comparsa: ComparsaResponse; isAdmin: boolean }) {
  const { t } = useTranslation('catalog');
  const update = useUpdateComparsa();
  const refresh = useRefreshAfterChange(comparsa.id);
  const values = useMemo<ComparsaValues>(
    () => ({ name: comparsa.name, side: comparsa.side }),
    [comparsa.name, comparsa.side],
  );
  const form = useAppForm<ComparsaValues, unknown, ComparsaInput>({
    resolver: zodResolver(comparsaSchema),
    defaultValues: values,
  });

  // The comparsa API has no version: the last save wins (design D9).
  const save = async (submitted: ComparsaInput): Promise<EditResult> => {
    try {
      await update.mutateAsync({ id: comparsa.id, data: submitted });
    } catch (error) {
      if (problemCode(error) === 'comparsas.notFound') {
        await refresh();
        return { status: 'rejected', reason: problemMessage(t, error) };
      }
      return applyFieldErrors(error, COMPARSA_FIELDS, form.setError, COMPARSA_CONFLICTS)
        ? { status: 'kept' }
        : { status: 'rejected', reason: problemMessage(t, error) };
    }
    await refresh();
    return { status: 'saved' };
  };

  return (
    <SectionCard
      title={t('comparsas.form.section')}
      action={
        isAdmin && (
          <EditSheet
            title={t('comparsas.detail.data.edit')}
            sectionName={t('comparsas.detail.data.name')}
            form={form}
            values={values}
            onSave={save}
          >
            <ComparsaFields control={form.control} />
          </EditSheet>
        )
      }
    >
      <DescriptionList
        items={[
          { term: t('comparsas.form.name'), value: comparsa.name },
          { term: t('comparsas.form.side'), value: <CategoryTag category="side" value={comparsa.side} /> },
        ]}
      />
    </SectionCard>
  );
}

/** The FiringChiefs of the comparsa (Admins only: never mounted for a FiringChief, design D8). */
function FiringChiefs({ comparsa, chiefs }: { comparsa: ComparsaResponse; chiefs: ChiefsQuery }) {
  return (
    <FiringChiefsSection
      comparsa={comparsa}
      chiefs={chiefs.rows ?? NO_CHIEFS}
      isLoading={chiefs.isPending}
      error={chiefs.error}
    />
  );
}

interface ChiefsQuery {
  rows: FiringChiefResponse[] | undefined;
  isPending: boolean;
  error: unknown;
}

/** Deactivate, reactivate and delete, from "More actions" (spec: Action hierarchy). */
function useComparsaActions(comparsa: ComparsaResponse, chiefs: number | undefined, announce: Announce) {
  const { t } = useTranslation('catalog');
  const { mutateAsync: deactivate } = useDeactivateComparsa();
  const { mutateAsync: reactivate } = useReactivateComparsa();
  const { mutateAsync: remove } = useDeleteComparsa();
  const id = comparsa.id;
  return useLifecycleActions({
    active: comparsa.active,
    texts: {
      deactivate: t('comparsas.actions.deactivate'),
      deactivateTitle: t('comparsas.actions.deactivateTitle', { name: comparsa.name }),
      deactivateDescription: t('comparsas.actions.deactivateDescription'),
      deactivated: t('comparsas.actions.deactivated'),
      reactivate: t('comparsas.actions.reactivate'),
      reactivated: t('comparsas.actions.reactivated'),
      delete: t('comparsas.actions.delete'),
      deleteTitle: t('comparsas.actions.deleteTitle', { name: comparsa.name }),
      // Not yet known (loading or failed): never claim that nobody loses access (WCAG 3.3.4).
      deleteDescription:
        chiefs === undefined
          ? t('comparsas.actions.deleteDescriptionUnknown')
          : chiefs === 0
            ? t('comparsas.actions.deleteDescriptionNone')
            : t('comparsas.actions.deleteDescription', { count: chiefs }),
      deleted: t('comparsas.actions.deleted'),
    },
    deactivate: () => deactivate({ id }),
    reactivate: () => reactivate({ id }),
    remove: () => remove({ id }),
    refresh: useRefreshAfterChange(id),
    announce,
    explain: (error) => problemMessage(t, error),
    listPath: '/comparsas',
    listKey: getListComparsasQueryKey(),
    recordKeys: [getGetComparsaQueryKey(id), getListFiringChiefsQueryKey(id)],
  });
}

function AdminDetail({
  comparsa,
  announce,
  notice,
}: {
  comparsa: ComparsaResponse;
  announce: Announce;
  notice: ReactNode;
}) {
  const firingChiefs = useListFiringChiefs(comparsa.id);
  const chiefs: ChiefsQuery = {
    rows: firingChiefs.data?.data as FiringChiefResponse[] | undefined,
    isPending: firingChiefs.isPending,
    error: firingChiefs.error ?? undefined,
  };
  const actions = useComparsaActions(comparsa, chiefs.rows?.length, announce);
  const refreshAfterChange = useRefreshAfterChange(comparsa.id);
  // Spec "Badge screens": every arquebusier of the comparsa, with the counts of incomplete badges.
  const arquebusiers = useListArquebusiers({ comparsaId: comparsa.id });
  const badgeRows = (arquebusiers.data?.data ?? []) as ArquebusierRowResponse[];
  return (
    <DetailLayout
      comparsa={comparsa}
      notice={notice}
      actions={
        badgeRows.length > 0 && (
          <BadgeSheet
            batch={{ kind: 'comparsa', comparsaId: comparsa.id, comparsaName: comparsa.name }}
            rows={badgeRows}
            context={comparsa.name}
            onUnknownIds={() => undefined}
          />
        )
      }
      moreActions={actions.items}
      moreActionsRef={actions.moreActions}
    >
      <DataSection comparsa={comparsa} isAdmin />
      <ComparsaLogoSection comparsa={comparsa} onChanged={refreshAfterChange} />
      <FiringChiefs comparsa={comparsa} chiefs={chiefs} />
      {actions.dialogs}
    </DetailLayout>
  );
}

function DetailLayout({
  comparsa,
  notice,
  actions,
  moreActions,
  moreActionsRef,
  children,
}: {
  comparsa: ComparsaResponse;
  /** The outcome of an action, under the header. */
  notice: ReactNode;
  /** Actions beside the name, e.g. "Print badges" (Admins). */
  actions?: ReactNode;
  moreActions?: MoreAction[];
  moreActionsRef?: Ref<HTMLButtonElement>;
  children: ReactNode;
}) {
  const { t } = useTranslation('catalog');
  return (
    <>
      <RecordHeader
        // Decorative: the name is the heading right beside it (spec: Logo display).
        media={<ComparsaLogo src={logoUrl(comparsa.id, comparsa.logo)} size="lg" />}
        back={{ to: '/comparsas', label: t('comparsas.detail.back') }}
        context={<CategoryTag category="side" value={comparsa.side} />}
        name={comparsa.name}
        statuses={<StatusBadge kind="catalog" value={comparsa.active ? 'ACTIVE' : 'INACTIVE'} />}
        actions={actions}
        moreActions={moreActions}
        moreActionsRef={moreActionsRef}
      />
      {notice}
      {!comparsa.active && (
        <AlertBanner severity="info" className="max-w-form" live={false}>
          {t('comparsas.detail.inactiveNotice')}
        </AlertBanner>
      )}
      <SectionGrid>{children}</SectionGrid>
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
  if (comparsa.isError && !details) {
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

  const banner = <NoticeBanner notice={notice} />;
  return session.account?.role === 'ADMIN' ? (
    <AdminDetail comparsa={details} announce={announce} notice={banner} />
  ) : (
    <DetailLayout comparsa={details} notice={banner}>
      <DataSection comparsa={details} isAdmin={false} />
    </DetailLayout>
  );
}

/** Specs "Comparsa management by Admins" and "Comparsa visibility": one comparsa, in read mode. */
export function ComparsaDetailPage() {
  const { id = '' } = useParams();
  return <ComparsaDetail key={id} id={id} />;
}
