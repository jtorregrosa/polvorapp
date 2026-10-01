import { zodResolver } from '@hookform/resolvers/zod';
import { useMemo } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { useGetArquebusier, useUpdateArquebusier } from '@/api/generated/arquebusiers/arquebusiers';
import type { ArquebusierResponse } from '@/api/generated/model';
import { ApiProblemError } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { Form } from '@/components/app/FormField';
import { PageHeader } from '@/components/app/PageHeader';
import { StatusBadge } from '@/components/app/StatusBadge';
import { useSession } from '@/features/identity-access/session';
import { NotFoundPage } from '@/features/platform/pages/NotFoundPage';
import { useNotice, type Announce } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import {
  ARQUEBUSIER_FIELDS,
  arquebusierSchema,
  requestFields,
  valuesOf,
  type ArquebusierValues,
} from '../arquebusierSchema';
import { useRefreshArquebusier } from '../hooks';
import { applyFieldErrors, problemCode, problemMessage } from '../problems';
import { ArquebusierActions } from '../components/ArquebusierActions';
import { LoadFailure } from '../components/LoadFailure';
import { OwnedWeaponsSection } from '../components/OwnedWeaponsSection';
import { ArquebusierFields } from './ArquebusierFields';

function EditForm({ arquebusier, announce }: { arquebusier: ArquebusierResponse; announce: Announce }) {
  const { t } = useTranslation('registry');
  const isAdmin = useSession().account?.role === 'ADMIN';
  const update = useUpdateArquebusier();
  const refresh = useRefreshArquebusier(arquebusier.id);
  const values = useMemo(() => valuesOf(arquebusier), [arquebusier]);
  // Follows the server's values, but a refresh never discards what the user is typing.
  const form = useForm<ArquebusierValues>({
    resolver: zodResolver(arquebusierSchema),
    values,
    resetOptions: { keepDirtyValues: true },
  });

  const onSubmit = async (submitted: ArquebusierValues): Promise<void> => {
    try {
      await update.mutateAsync({
        id: arquebusier.id,
        data: { ...requestFields(submitted), version: arquebusier.version },
      });
    } catch (error) {
      if (problemCode(error) === 'arquebusiers.notFound') {
        // Deleted or moved out of scope meanwhile: the refreshed page says it is not found.
        await refresh();
        return;
      }
      if (problemCode(error) === 'arquebusiers.modified') {
        // Spec: an outdated edit is explained and the current data loaded; the user's edits stay.
        announce('error', t('form.modified'));
        await refresh();
        return;
      }
      const placed = applyFieldErrors(error, ARQUEBUSIER_FIELDS, form.setError, {
        isAdmin,
        conflicts: {
          'arquebusiers.nationalIdTaken': 'nationalId',
          'arquebusiers.federationIdTaken': 'federationId',
        },
      });
      if (!placed) announce('error', problemMessage(t, error, { isAdmin }));
      return;
    }
    form.reset(submitted);
    announce('success', t('form.saved'));
    await refresh();
  };

  return (
    <Form form={form} onSubmit={onSubmit} className="max-w-xl">
      <ArquebusierFields form={form} />
      <Button type="submit" pending={update.isPending}>
        {t('form.save')}
      </Button>
    </Form>
  );
}

function ArquebusierDetail({ id }: { id: string }) {
  const { t } = useTranslation('registry');
  const arquebusier = useGetArquebusier(id, { query: { retry: false } });
  const details = arquebusier.data?.data as ArquebusierResponse | undefined;
  const name = details ? `${details.firstName} ${details.lastName}` : undefined;
  useDocumentTitle(name ?? t('arquebusiers.title'));
  const [notice, announce] = useNotice();
  const back = { to: '/arquebusiers', label: t('detail.back') };
  const refresh = useRefreshArquebusier(id);

  if (arquebusier.error instanceof ApiProblemError && arquebusier.error.status === 404) {
    // Unknown and out of scope look the same (BR-12).
    return <NotFoundPage />;
  }
  if (arquebusier.isError && !details) {
    return (
      <>
        <PageHeader title={t('arquebusiers.title')} back={back} />
        <LoadFailure error={arquebusier.error} onRetry={() => arquebusier.refetch()} />
      </>
    );
  }
  if (!details || !name) {
    return <PageHeader title={t('detail.loading')} back={back} />;
  }

  return (
    <>
      <PageHeader
        title={name}
        description={details.comparsaName}
        back={back}
        actions={<StatusBadge kind="arquebusier" value={details.status} />}
      />
      <div className="grid gap-10">
        {notice && (
          <AlertBanner key={notice.id} severity={notice.severity} className="max-w-xl" focusOnMount>
            {notice.text}
          </AlertBanner>
        )}
        {/* A failed refresh keeps what was loaded: the page stays usable and says it may be outdated. */}
        {arquebusier.isError && (
          <LoadFailure
            className="max-w-xl"
            error={arquebusier.error}
            consequence={t('load.stale')}
            onRetry={() => arquebusier.refetch()}
          />
        )}
        {!details.comparsaActive && (
          <AlertBanner severity="info" className="max-w-xl">
            {t('detail.inactiveComparsa', { name: details.comparsaName })}
          </AlertBanner>
        )}
        <EditForm arquebusier={details} announce={announce} />
        <OwnedWeaponsSection arquebusier={details} announce={announce} onChanged={refresh} />
        <ArquebusierActions arquebusier={details} announce={announce} onChanged={refresh} />
      </div>
    </>
  );
}

/** Specs "Registering and editing arquebusiers" and "Registry screens": one arquebusier. */
export function ArquebusierDetailPage() {
  const { id = '' } = useParams();
  return <ArquebusierDetail key={id} id={id} />;
}
