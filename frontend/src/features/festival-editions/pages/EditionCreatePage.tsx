import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useWatch } from 'react-hook-form';
import { Link, useNavigate } from 'react-router';
import {
  getListEditionsQueryKey,
  useCreateEdition,
  useListEditions,
} from '@/api/generated/editions/editions';
import type { EditionResponse, EditionRowResponse } from '@/api/generated/model';
import { responseData } from '@/api/http';
import { ActionBar } from '@/components/app/ActionBar';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { DateInput } from '@/components/app/DateInput';
import { Form, FormField } from '@/components/app/FormField';
import { FormLayout } from '@/components/app/FormLayout';
import { PageHeader } from '@/components/app/PageHeader';
import { TextInput } from '@/components/app/TextInput';
import { useAppForm } from '@/components/app/use-app-form';
import { noticeState } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import {
  createEditionSchema,
  EMPTY_CREATE_EDITION,
  type CreateEditionInput,
  type CreateEditionValues,
} from '../editionSchema';
import { useExplain } from '../components/useSaveEdition';
import { copySource } from '../editionText';
import { applyFieldErrors } from '../problems';

const FIELDS = ['year', 'festivalStartsOn', 'festivalEndsOn'] as const;

/**
 * Spec "Editions screens" and "New editions start from the previous one": an Admin creates a draft
 * with its year and festival dates (form template). The form says which edition's prices and
 * rental models the server will copy; the server decides.
 */
export function EditionCreatePage() {
  const { t } = useTranslation('editions');
  const explain = useExplain();
  useDocumentTitle(t('create.title'));
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const create = useCreateEdition();
  const editions = useListEditions();
  const form = useAppForm<CreateEditionValues, unknown, CreateEditionInput>({
    resolver: zodResolver(createEditionSchema),
    defaultValues: EMPTY_CREATE_EDITION,
  });
  const typedYear = useWatch({ control: form.control, name: 'year' });
  const year = /^\d{4}$/.test(typedYear.trim()) ? Number(typedYear.trim()) : undefined;
  const source = copySource((editions.data?.data ?? []) as EditionRowResponse[], year);

  const onSubmit = async (values: CreateEditionInput): Promise<void> => {
    let created: EditionResponse;
    try {
      created = responseData(await create.mutateAsync({ data: values }));
    } catch (error) {
      if (!applyFieldErrors(error, FIELDS, form.setError, { 'editions.yearTaken': 'year' })) {
        form.setError('root.server', { type: 'server', message: explain(error) });
      }
      return;
    }
    void queryClient.invalidateQueries({ queryKey: getListEditionsQueryKey() });
    await navigate(`/editions/${created.id}`, {
      state: noticeState(t('create.created', { year: created.year })),
    });
  };

  return (
    <>
      <PageHeader
        title={t('create.title')}
        description={t('create.description')}
        back={{ to: '/editions', label: t('create.back') }}
      />
      <Form form={form} onSubmit={onSubmit}>
        <FormLayout
          sections={[
            {
              id: 'edition',
              title: t('create.section'),
              content: (
                <>
                  <FormField
                    control={form.control}
                    name="year"
                    label={t('create.year')}
                    description={t('create.yearHelp')}
                    width="id"
                  >
                    {(field) => <TextInput {...field} inputMode="numeric" autoComplete="off" maxLength={4} />}
                  </FormField>
                  <FormField
                    control={form.control}
                    name="festivalStartsOn"
                    label={t('create.festivalStartsOn')}
                    width="short"
                  >
                    {(field) => <DateInput {...field} />}
                  </FormField>
                  <FormField
                    control={form.control}
                    name="festivalEndsOn"
                    label={t('create.festivalEndsOn')}
                    width="short"
                  >
                    {(field) => <DateInput {...field} />}
                  </FormField>
                  {editions.isSuccess && (
                    <AlertBanner severity="info" live={false} className="max-w-form">
                      {source === undefined
                        ? t('create.copyNothing')
                        : t('create.copyFrom', { year: source })}
                    </AlertBanner>
                  )}
                </>
              ),
            },
          ]}
          actions={
            <ActionBar
              secondary={
                <Button asChild variant="secondary">
                  <Link to="/editions">{t('create.cancel')}</Link>
                </Button>
              }
              primary={
                <Button type="submit" pending={create.isPending}>
                  {t('create.submit')}
                </Button>
              }
            />
          }
        />
      </Form>
    </>
  );
}
