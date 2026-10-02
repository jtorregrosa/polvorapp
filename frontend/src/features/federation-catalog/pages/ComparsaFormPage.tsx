import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router';
import { getListComparsasQueryKey, useCreateComparsa } from '@/api/generated/comparsas/comparsas';
import type { ComparsaResponse } from '@/api/generated/model';
import { responseData } from '@/api/http';
import { ActionBar } from '@/components/app/ActionBar';
import { Button } from '@/components/app/Button';
import { Form } from '@/components/app/FormField';
import { FormLayout } from '@/components/app/FormLayout';
import { PageHeader } from '@/components/app/PageHeader';
import { useAppForm } from '@/components/app/use-app-form';
import { noticeState } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { applyFieldErrors, problemMessage } from '../problems';
import { ComparsaFields } from './ComparsaFields';
import {
  COMPARSA_CONFLICTS,
  COMPARSA_FIELDS,
  comparsaSchema,
  type ComparsaInput,
  type ComparsaValues,
} from './comparsaSchema';

/** Spec "Comparsa management by Admins": an Admin creates a comparsa (form template). */
export function ComparsaFormPage() {
  const { t } = useTranslation('catalog');
  useDocumentTitle(t('comparsas.form.newTitle'));
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const create = useCreateComparsa();
  const form = useAppForm<ComparsaValues, unknown, ComparsaInput>({
    resolver: zodResolver(comparsaSchema),
    defaultValues: { name: '', side: '' },
  });

  const onSubmit = async (values: ComparsaInput): Promise<void> => {
    let created: ComparsaResponse;
    try {
      created = responseData(await create.mutateAsync({ data: values }));
    } catch (error) {
      if (!applyFieldErrors(error, COMPARSA_FIELDS, form.setError, COMPARSA_CONFLICTS)) {
        // A refusal about no field is listed in the error summary, which takes focus.
        form.setError('root.server', { type: 'server', message: problemMessage(t, error) });
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
      <Form form={form} onSubmit={onSubmit}>
        <FormLayout
          sections={[
            {
              id: 'comparsa',
              title: t('comparsas.form.section'),
              content: <ComparsaFields control={form.control} />,
            },
          ]}
          actions={
            <ActionBar
              secondary={
                <Button asChild variant="secondary">
                  <Link to="/comparsas">{t('comparsas.form.cancel')}</Link>
                </Button>
              }
              primary={
                <Button type="submit" pending={create.isPending}>
                  {t('comparsas.form.create')}
                </Button>
              }
            />
          }
        />
      </Form>
    </>
  );
}
