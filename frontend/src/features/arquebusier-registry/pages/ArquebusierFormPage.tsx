import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import {
  getGetArquebusierQueryKey,
  getListArquebusiersQueryKey,
  useRegisterArquebusier,
  type registerArquebusierResponse,
} from '@/api/generated/arquebusiers/arquebusiers';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import type { ArquebusierResponse, ComparsaResponse } from '@/api/generated/model';
import { responseData } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { Form } from '@/components/app/FormField';
import { PageHeader } from '@/components/app/PageHeader';
import { useSession } from '@/features/identity-access/session';
import { noticeState } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import {
  ARQUEBUSIER_FIELDS,
  EMPTY_ARQUEBUSIER,
  registerSchema,
  requestFields,
  type ArquebusierValues,
} from '../arquebusierSchema';
import { LoadFailure } from '../components/LoadFailure';
import { applyFieldErrors, problemMessage } from '../problems';
import { ArquebusierFields } from './ArquebusierFields';

/** Spec "Registering and editing arquebusiers": registers an arquebusier in an active comparsa in scope. */
export function ArquebusierFormPage() {
  const { t } = useTranslation('registry');
  useDocumentTitle(t('form.newTitle'));
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const session = useSession();
  const isAdmin = session.account?.role === 'ADMIN';
  const register = useRegisterArquebusier();
  const [failure, setFailure] = useState<unknown>();
  // Without includeInactive the API lists only active comparsas, within the caller's scope.
  const comparsas = useListComparsas(undefined, { query: { enabled: session.status === 'signedIn' } });
  const active = useMemo(
    () => ((comparsas.data?.data ?? []) as ComparsaResponse[]).filter((comparsa) => comparsa.active),
    [comparsas.data],
  );
  const noComparsa = comparsas.isSuccess && active.length === 0;
  const form = useForm<ArquebusierValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: EMPTY_ARQUEBUSIER,
  });

  // Spec "Registry screens": the only comparsa in scope is pre-selected.
  useEffect(() => {
    if (active.length === 1 && form.getValues('comparsaId') === '') {
      form.setValue('comparsaId', active[0]?.id ?? '');
    }
  }, [active, form]);

  const onSubmit = async (values: ArquebusierValues): Promise<void> => {
    let response: registerArquebusierResponse;
    setFailure(undefined);
    try {
      response = await register.mutateAsync({
        data: { comparsaId: values.comparsaId, ...requestFields(values) },
      });
    } catch (error) {
      const placed = applyFieldErrors(error, ARQUEBUSIER_FIELDS, form.setError, {
        isAdmin,
        conflicts: {
          'arquebusiers.nationalIdTaken': 'nationalId',
          'arquebusiers.federationIdTaken': 'federationId',
          'arquebusiers.comparsaNotFound': 'comparsaId',
          'arquebusiers.comparsaInactive': 'comparsaId',
        },
      });
      if (!placed) setFailure(error);
      return;
    }
    void queryClient.invalidateQueries({ queryKey: getListArquebusiersQueryKey() });
    let created: ArquebusierResponse;
    try {
      created = responseData(response);
    } catch {
      // Registered, but the answer had no body: show the list, where the new arquebusier is.
      await navigate('/arquebusiers', { state: noticeState(t('form.registeredUnnamed')) });
      return;
    }
    // The detail page opens on the data just returned, so its notice is shown (and focused) at once.
    queryClient.setQueryData(getGetArquebusierQueryKey(created.id), { ...response, status: 200 });
    await navigate(`/arquebusiers/${created.id}`, {
      state: noticeState(t('form.registered', { name: `${created.firstName} ${created.lastName}` })),
    });
  };

  return (
    <>
      <PageHeader
        title={t('form.newTitle')}
        description={t('form.newDescription')}
        back={{ to: '/arquebusiers', label: t('detail.back') }}
      />
      {comparsas.isError && (
        <LoadFailure
          className="mb-4 max-w-xl"
          error={comparsas.error}
          consequence={t('load.comparsas')}
          onRetry={() => comparsas.refetch()}
        />
      )}
      {noComparsa && (
        <AlertBanner severity="info" className="mb-4 max-w-xl">
          {t('form.noActiveComparsa')}
        </AlertBanner>
      )}
      <Form form={form} onSubmit={onSubmit} className="max-w-xl">
        <ArquebusierFields form={form} comparsas={active} />
        {failure !== undefined && (
          <AlertBanner severity="error">{problemMessage(t, failure, { isAdmin })}</AlertBanner>
        )}
        <Button type="submit" pending={register.isPending} disabled={comparsas.isError || noComparsa}>
          {t('form.register')}
        </Button>
      </Form>
    </>
  );
}
