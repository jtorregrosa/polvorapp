import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { Download } from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { useWatch } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router';
import {
  getDownloadArquebusierImportTemplateUrl,
  getListArquebusiersQueryKey,
  importArquebusiers,
  usePreviewArquebusierImport,
} from '@/api/generated/arquebusiers/arquebusiers';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import type { ArquebusierImportReport, ComparsaResponse } from '@/api/generated/model';
import { apiDownload, responseData } from '@/api/http';
import { ActionBar } from '@/components/app/ActionBar';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { ConfirmFailure } from '@/components/app/confirm-failure';
import { FileField } from '@/components/app/FileField';
import { Form, FormField } from '@/components/app/FormField';
import { FormLayout } from '@/components/app/FormLayout';
import { PageHeader } from '@/components/app/PageHeader';
import { SelectInput } from '@/components/app/SelectInput';
import { useAppForm } from '@/components/app/use-app-form';
import { invalidateInsights } from '@/features/compliance-insights/queries';
import { useSession } from '@/features/identity-access/session';
import { saveFile } from '@/lib/download';
import { noticeState } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { ImportReport } from '../components/ImportReport';
import { LoadFailure } from '../components/LoadFailure';
import {
  EMPTY_IMPORT,
  fileProblemText,
  IMPORT_FILE_RULES,
  importSchema,
  rejectedReport,
  type ImportValues,
} from '../import';
import { problemCode, problemMessage } from '../problems';

/** A report and what it was made from: changing the comparsa or the file discards it. */
interface Checked {
  comparsaId: string;
  file: File;
  report: ArquebusierImportReport;
  /** The report of a refused import: the registry changed after the check. */
  changed: boolean;
  /** A new one per report, so the report is shown afresh: its outcome focused, its filter reset. */
  id: number;
}

const COMPARSA_PROBLEMS = new Set(['arquebusiers.comparsaNotFound', 'arquebusiers.comparsaInactive']);

/**
 * Spec "Import screen" (UC-09, Admins only): choose the comparsa, download the template, choose the
 * file, check it and read the report, and import it once no row has an error, after a confirmation.
 */
export function ArquebusierImportPage() {
  const { t } = useTranslation(['registry', 'ui']);
  useDocumentTitle(t('import.title'));
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const session = useSession();
  const preview = usePreviewArquebusierImport();
  // Without includeInactive the API lists only active comparsas, the only ones an import accepts.
  const comparsas = useListComparsas(undefined, { query: { enabled: session.status === 'signedIn' } });
  const active = useMemo(
    () => ((comparsas.data?.data ?? []) as ComparsaResponse[]).filter((comparsa) => comparsa.active),
    [comparsas.data],
  );
  const form = useAppForm<ImportValues>({ resolver: zodResolver(importSchema), defaultValues: EMPTY_IMPORT });
  const [comparsaId, file] = useWatch({ control: form.control, name: ['comparsaId', 'file'] });
  const [checked, setChecked] = useState<Checked>();
  const [downloading, setDownloading] = useState(false);
  const [templateFailed, setTemplateFailed] = useState(false);
  const reports = useRef(0);
  // What the confirmed import ended with, read once the dialog has closed.
  const outcome = useRef<{
    imported?: { count: number; comparsaId: string };
    refused?: ArquebusierImportReport;
  }>({});

  // A report only stands for the comparsa and file it was made from: changing either discards it,
  // and a check answered after such a change is not shown.
  const current = checked?.comparsaId === comparsaId && checked.file === file ? checked : undefined;
  const discardReport = () => {
    setChecked(undefined);
  };
  const comparsaName = active.find((comparsa) => comparsa.id === comparsaId)?.name ?? '';

  const downloadTemplate = async () => {
    setTemplateFailed(false);
    setDownloading(true);
    try {
      const template = await apiDownload(getDownloadArquebusierImportTemplateUrl());
      saveFile(template.blob, template.fileName ?? 'polvorapp-arquebusiers-template.xlsx');
    } catch {
      setTemplateFailed(true);
    } finally {
      setDownloading(false);
    }
  };

  const onCheck = async (values: ImportValues): Promise<void> => {
    if (!values.file) return;
    try {
      const response = await preview.mutateAsync({
        data: { comparsaId: values.comparsaId, file: values.file },
      });
      setChecked({
        comparsaId: values.comparsaId,
        file: values.file,
        report: responseData(response),
        changed: false,
        id: ++reports.current,
      });
    } catch (error) {
      const fileText = fileProblemText(t, error);
      const code = problemCode(error);
      if (fileText) {
        // Already translated: FormField shows a message that is not a key as it is.
        form.setError('file', { type: 'server', message: fileText });
      } else if (code && COMPARSA_PROBLEMS.has(code)) {
        form.setError('comparsaId', { type: 'server', message: `registry:errors.${code}` });
      } else {
        form.setError('root.server', {
          type: 'server',
          message: problemMessage(t, error, { isAdmin: true }),
        });
      }
    }
  };

  const onImport = async (): Promise<void> => {
    outcome.current = {};
    if (!current) return;
    try {
      const response = await importArquebusiers({ comparsaId: current.comparsaId, file: current.file });
      const result = responseData(response);
      outcome.current = { imported: { count: result.importedCount, comparsaId: result.comparsaId } };
    } catch (error) {
      // The registry changed since the check: the dialog closes, then the new report replaces the old one.
      const refused = rejectedReport(error);
      if (refused) {
        outcome.current = { refused };
        return;
      }
      throw new ConfirmFailure(problemMessage(t, error, { isAdmin: true }));
    }
  };

  // Runs once the dialog has closed, so focus can move to what replaces it (WCAG 2.4.3).
  const afterImport = async () => {
    const { imported: done, refused } = outcome.current;
    if (refused && current) {
      // A new report: its outcome is focused when it appears.
      setChecked({ ...current, report: refused, changed: true, id: ++reports.current });
      return;
    }
    if (!done) return;
    void invalidateInsights(queryClient);
    await queryClient.invalidateQueries({ queryKey: getListArquebusiersQueryKey() });
    await navigate(`/arquebusiers?comparsaId=${encodeURIComponent(done.comparsaId)}`, {
      state: noticeState(t('import.imported', { count: done.count, comparsa: comparsaName })),
    });
  };

  const canImport = current?.report.errorRowCount === 0;
  const count = current?.report.validCount ?? 0;

  return (
    <>
      <PageHeader
        title={t('import.title')}
        description={t('import.description')}
        back={{ to: '/arquebusiers', label: t('detail.back') }}
      />
      {comparsas.isError && (
        <LoadFailure
          className="max-w-form"
          error={comparsas.error}
          consequence={t('load.comparsas')}
          onRetry={() => comparsas.refetch()}
        />
      )}
      {comparsas.isSuccess && active.length === 0 && (
        <AlertBanner severity="info" className="max-w-form">
          {t('form.noActiveComparsa')}
        </AlertBanner>
      )}
      <Form form={form} onSubmit={onCheck}>
        <FormLayout
          sections={[
            {
              id: 'comparsa',
              title: t('import.comparsa.title'),
              description: t('import.comparsa.description'),
              content: (
                <FormField
                  control={form.control}
                  name="comparsaId"
                  label={t('import.comparsa.label')}
                  width="name"
                >
                  {(field) => (
                    <SelectInput
                      {...field}
                      onChange={(event) => {
                        field.onChange(event);
                        discardReport();
                      }}
                      placeholder={t('validation.choice')}
                      options={active.map((comparsa) => ({ value: comparsa.id, label: comparsa.name }))}
                    />
                  )}
                </FormField>
              ),
            },
            {
              id: 'file',
              title: t('import.file.title'),
              description: t('import.file.description'),
              content: (
                <>
                  <div className="flex flex-col items-start gap-2">
                    <Button
                      type="button"
                      variant="secondary"
                      icon={Download}
                      pending={downloading}
                      onClick={() => {
                        void downloadTemplate();
                      }}
                    >
                      {t('import.file.template')}
                    </Button>
                    {/* Announced as an alert; focus stays on the button to try again. */}
                    {templateFailed && (
                      <AlertBanner severity="error">{t('import.file.templateFailed')}</AlertBanner>
                    )}
                  </div>
                  <FormField
                    control={form.control}
                    name="file"
                    label={t('import.file.label')}
                    description={t('import.file.hint')}
                  >
                    {(field) => (
                      <FileField
                        {...field}
                        accept={IMPORT_FILE_RULES.accept}
                        onChange={(chosen) => {
                          field.onChange(chosen);
                          discardReport();
                          if (chosen) void form.trigger('file');
                          else form.clearErrors('file');
                        }}
                      />
                    )}
                  </FormField>
                </>
              ),
            },
            ...(current
              ? [
                  {
                    id: 'report',
                    title: t('import.report.title'),
                    description: t('import.report.description'),
                    content: (
                      <ImportReport key={current.id} report={current.report} changed={current.changed} />
                    ),
                  },
                ]
              : []),
          ]}
          actions={
            <ActionBar
              secondary={
                <>
                  <Button asChild variant="secondary">
                    <Link to="/arquebusiers">{t('form.cancel')}</Link>
                  </Button>
                  {canImport && (
                    <Button type="submit" variant="secondary" pending={preview.isPending}>
                      {t('import.file.check')}
                    </Button>
                  )}
                </>
              }
              primary={
                canImport ? (
                  <ConfirmDialog
                    tone="primary"
                    title={t('import.confirm.title', { count, comparsa: comparsaName })}
                    description={t('import.confirm.description')}
                    confirmLabel={t('import.confirm.confirm', { count })}
                    onConfirm={onImport}
                    onConfirmed={() => {
                      void afterImport();
                    }}
                    trigger={<Button type="button">{t('import.confirm.open', { count })}</Button>}
                  />
                ) : (
                  <Button
                    type="submit"
                    pending={preview.isPending}
                    disabled={comparsas.isError || (comparsas.isSuccess && active.length === 0)}
                  >
                    {t('import.file.check')}
                  </Button>
                )
              }
            />
          }
        />
      </Form>
    </>
  );
}
