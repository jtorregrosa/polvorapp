import { ArrowRightLeft, CirclePause, CirclePlay, Trash2 } from 'lucide-react';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { useGetArquebusier, useUpdateArquebusier } from '@/api/generated/arquebusiers/arquebusiers';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import type { ArquebusierResponse, ComparsaResponse } from '@/api/generated/model';
import { ApiProblemError, responseData } from '@/api/http';
import { AlertBanner, NoticeBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { PageHeader } from '@/components/app/PageHeader';
import { RecordHeader, type MoreAction } from '@/components/app/RecordHeader';
import { useSaveNotice } from '@/components/app/save-notice';
import { StatusBadge } from '@/components/app/StatusBadge';
import { useSession } from '@/features/identity-access/session';
import { ComplianceWarnings } from '@/features/compliance-insights/components/ComplianceWarnings';
import { NotFoundPage } from '@/features/platform/pages/NotFoundPage';
import { useNotice, type Announce, type Notice } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { requestFields, valuesOf } from '../arquebusierSchema';
import { DeleteDialog, TransferDialog } from '../components/ArquebusierActions';
import { IdPhoto } from '../components/ArquebusierPhotos';
import { ArquebusierSections } from '../components/ArquebusierSections';
import { RecordFacts } from '../components/ArquebusierSummary';
import { LoadFailure } from '../components/LoadFailure';
import { useRefreshArquebusier } from '../hooks';
import { licenseBadgeValue } from '../license-badge';
import { problemCode, problemMessage } from '../problems';

/**
 * Reserve or active again, and delete, from "More actions"; transfer is the header's action
 * (spec: Action hierarchy). A status change is announced without moving focus.
 */
function useArquebusierActions(details: ArquebusierResponse, name: string, announce: Announce) {
  const { t } = useTranslation('registry');
  const { t: tUi } = useTranslation('ui');
  const notify = useSaveNotice();
  const refresh = useRefreshArquebusier(details.id);
  const update = useUpdateArquebusier();
  const [dialog, setDialog] = useState<'transfer' | 'delete'>();
  const nextStatus = details.status === 'ACTIVE' ? 'RESERVE' : 'ACTIVE';

  const changeStatus = async () => {
    try {
      await update.mutateAsync({
        id: details.id,
        data: { ...requestFields({ ...valuesOf(details), status: nextStatus }), version: details.version },
      });
    } catch (error) {
      announce(
        'error',
        problemCode(error) === 'arquebusiers.modified' ? t('form.modified') : problemMessage(t, error),
      );
      await refresh();
      return;
    }
    await refresh();
    notify(t('detail.status.changed', { name, status: tUi(`status.arquebusier.${nextStatus}`) }));
  };

  const items: MoreAction[] = [
    {
      id: 'status',
      label: t(nextStatus === 'RESERVE' ? 'detail.status.toReserve' : 'detail.status.toActive'),
      icon: nextStatus === 'RESERVE' ? CirclePause : CirclePlay,
      // One change at a time: a second one would carry the same version and conflict.
      disabled: update.isPending,
      onSelect: () => {
        void changeStatus();
      },
    },
    {
      id: 'delete',
      label: t('delete.action'),
      icon: Trash2,
      destructive: true,
      onSelect: () => {
        setDialog('delete');
      },
    },
  ];
  return { items, dialog, setDialog, refresh };
}

interface RecordProps {
  details: ArquebusierResponse;
  notice: Notice | undefined;
  announce: Announce;
  /** The record as the server has it now, or undefined when it could not be loaded again. */
  reload: () => Promise<ArquebusierResponse | undefined>;
  /** A refresh failed: what is shown may be outdated. */
  staleError: unknown;
  onRetry: () => void;
}

function ArquebusierRecord({ details, notice, announce, reload, staleError, onRetry }: RecordProps) {
  const { t } = useTranslation('registry');
  const { t: tCatalog } = useTranslation('catalog');
  const isAdmin = useSession().account?.role === 'ADMIN';
  const name = `${details.firstName} ${details.lastName}`;
  const comparsas = useListComparsas({ includeInactive: true });
  const moreActions = useRef<HTMLButtonElement>(null);
  const transferButton = useRef<HTMLButtonElement>(null);
  const { items, dialog, setDialog, refresh } = useArquebusierActions(details, name, announce);
  const comparsa = ((comparsas.data?.data ?? []) as ComparsaResponse[]).find(
    (entry) => entry.id === details.comparsaId,
  );

  return (
    <>
      <RecordHeader
        back={{ to: '/arquebusiers', label: t('detail.back') }}
        media={<IdPhoto arquebusier={details} onChanged={refresh} />}
        context={
          comparsa ? `${details.comparsaName} · ${tCatalog(`side.${comparsa.side}`)}` : details.comparsaName
        }
        name={name}
        statuses={
          <>
            <StatusBadge kind="arquebusier" value={details.status} />
            {details.license ? (
              <>
                {/* The header has no field labels: the badge is named "License: …". */}
                <span className="sr-only">{t('form.license')}: </span>
                <StatusBadge
                  kind="license"
                  value={licenseBadgeValue(details.license.status, details.warnings)}
                />
              </>
            ) : (
              <span className="text-help text-muted-foreground">{t('form.noLicense')}</span>
            )}
            <span className="text-help text-muted-foreground">
              {t(details.trainingCompletedOn ? 'detail.courseState.done' : 'detail.courseState.missing')}
            </span>
          </>
        }
        actions={
          isAdmin && (
            <Button
              ref={transferButton}
              variant="secondary"
              icon={ArrowRightLeft}
              onClick={() => {
                setDialog('transfer');
              }}
            >
              {t('transfer.action')}
            </Button>
          )
        }
        moreActions={items}
        moreActionsRef={moreActions}
      />
      <NoticeBanner notice={notice} />
      {/* A failed refresh keeps what was loaded: the page stays usable and says it may be outdated. */}
      {staleError !== null && staleError !== undefined && (
        <LoadFailure error={staleError} consequence={t('load.stale')} onRetry={onRetry} />
      )}
      {!details.comparsaActive && (
        <AlertBanner severity="info" className="max-w-form" live={false}>
          {t('detail.inactiveComparsa', { name: details.comparsaName })}
        </AlertBanner>
      )}
      <ComplianceWarnings
        warnings={details.warnings}
        licenseExpiresOn={details.license?.expiresOn ?? null}
        age={details.age}
      />
      <RecordFacts arquebusier={details} />
      <ArquebusierSections arquebusier={details} reload={reload} announce={announce} />
      {isAdmin && (
        <TransferDialog
          arquebusier={details}
          open={dialog === 'transfer'}
          onOpenChange={(open) => {
            setDialog(open ? 'transfer' : undefined);
          }}
          returnFocus={transferButton}
          announce={announce}
          onChanged={refresh}
        />
      )}
      <DeleteDialog
        arquebusier={details}
        open={dialog === 'delete'}
        onOpenChange={(open) => {
          setDialog(open ? 'delete' : undefined);
        }}
        returnFocus={moreActions}
      />
    </>
  );
}

function ArquebusierDetail({ id }: { id: string }) {
  const { t } = useTranslation('registry');
  const query = useGetArquebusier(id, { query: { retry: false } });
  const details = query.data?.data as ArquebusierResponse | undefined;
  useDocumentTitle(details ? `${details.firstName} ${details.lastName}` : t('arquebusiers.title'));
  const [notice, announce] = useNotice();
  const back = { to: '/arquebusiers', label: t('detail.back') };

  if (query.error instanceof ApiProblemError && query.error.status === 404) {
    // Unknown and out of scope look the same (BR-12).
    return <NotFoundPage />;
  }
  if (query.isError && !details) {
    return (
      <>
        <PageHeader title={t('arquebusiers.title')} back={back} />
        <LoadFailure error={query.error} onRetry={() => query.refetch()} />
      </>
    );
  }
  if (!details) {
    return <PageHeader title={t('detail.loading')} back={back} />;
  }

  const reload = async () => {
    const result = await query.refetch();
    if (result.isError || !result.data) return undefined;
    try {
      return responseData(result.data);
    } catch {
      return undefined; // An empty body: treated as not reloaded.
    }
  };
  return (
    <ArquebusierRecord
      details={details}
      notice={notice}
      announce={announce}
      reload={reload}
      staleError={query.isError ? query.error : undefined}
      onRetry={() => void query.refetch()}
    />
  );
}

/** Specs "Registering and editing arquebusiers" and "Registry screens": one arquebusier, read-only. */
export function ArquebusierDetailPage() {
  const { id = '' } = useParams();
  return <ArquebusierDetail key={id} id={id} />;
}
