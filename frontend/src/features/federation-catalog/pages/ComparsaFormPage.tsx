import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { getListComparsasQueryKey, useCreateComparsa } from '@/api/generated/comparsas/comparsas';
import type { ComparsaResponse } from '@/api/generated/model';
import { responseData } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { Form } from '@/components/app/FormField';
import { PageHeader } from '@/components/app/PageHeader';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { noticeState } from '@/lib/notices';
import { applyFieldErrors, problemMessage } from '../problems';
import { ComparsaFields } from './ComparsaFields';
import {
  COMPARSA_CONFLICTS,
  COMPARSA_FIELDS,
  comparsaSchema,
  type ComparsaInput,
  type ComparsaValues,
} from './comparsaSchema';

/** Spec "Comparsa management by Admins": an Admin creates a comparsa. */
export function ComparsaFormPage() {
  const { t } = useTranslation('catalog');
  useDocumentTitle(t('comparsas.form.newTitle'));
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const create = useCreateComparsa();
  const [failure, setFailure] = useState<unknown>();
  const form = useForm<ComparsaValues, unknown, ComparsaInput>({
    resolver: zodResolver(comparsaSchema),
    defaultValues: { name: '', side: '' },
  });

  const onSubmit = async (values: ComparsaInput): Promise<void> => {
    let created: ComparsaResponse;
    setFailure(undefined);
    try {
      created = responseData(await create.mutateAsync({ data: values }));
    } catch (error) {
      if (!applyFieldErrors(error, COMPARSA_FIELDS, form.setError, COMPARSA_CONFLICTS)) {
        setFailure(error);
      }
      return;
    }
    void queryClient.invalidateQueries({ queryKey: getListComparsasQueryKey() });
    await navigate(`/comparsas/${created.id}`, { state: noticeState(t('comparsas.form.created')) });
  };

  return (
    <>
      <PageHeader
        title={t('comparsas.form.newTitle')}
        description={t('comparsas.form.newDescription')}
        back={{ to: '/comparsas', label: t('comparsas.detail.back') }}
      />
      <Form form={form} onSubmit={onSubmit} className="max-w-xl">
        <ComparsaFields control={form.control} />
        {failure !== undefined && <AlertBanner severity="error">{problemMessage(t, failure)}</AlertBanner>}
        <Button type="submit" pending={create.isPending}>
          {t('comparsas.form.create')}
        </Button>
      </Form>
    </>
  );
}
