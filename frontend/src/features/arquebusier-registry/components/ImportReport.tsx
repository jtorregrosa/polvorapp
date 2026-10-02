import type { TFunction } from 'i18next';
import { CircleX, TriangleAlert } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { ArquebusierImportReport, ArquebusierImportRow } from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { CheckboxField } from '@/components/app/CheckboxField';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { StatCard } from '@/components/app/StatCard';
import { useFormatters } from '@/lib/format';
import { rowErrorText } from '../import';

interface ImportReportProps {
  report: ArquebusierImportReport;
  /** The report came back from a refused import: the registry changed since the check. */
  changed?: boolean;
}

/** "Last names, First name" as written in the file, or a placeholder when the row has none. */
function rowName(t: TFunction<['registry', 'ui']>, row: ArquebusierImportRow): string {
  return [row.lastName, row.firstName].filter(Boolean).join(', ') || t('import.report.noName');
}

/**
 * One reported row's problems, errors first, each in words with its column. Errors and warnings
 * differ by icon and by a spoken "Error"/"Warning", not only by colour (WCAG 1.4.1).
 */
function Problems({ row }: { row: ArquebusierImportRow }) {
  const { t } = useTranslation(['registry', 'ui']);
  return (
    <ul className="flex min-w-64 flex-col gap-1 whitespace-normal">
      {row.errors.map((error) => (
        <li key={`${error.field}-${error.reason}`} className="flex items-start gap-1.5 text-destructive">
          <CircleX aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          <span>
            <span className="sr-only">{`${t('ui:alert.error')}: `}</span>
            {rowErrorText(t, error.field, error.reason)}
          </span>
        </li>
      ))}
      {row.warnings.map((warning) => (
        <li key={warning} className="flex items-start gap-1.5 text-muted-foreground">
          <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          <span>{t('import.report.warning', { warning: t(`ui:status.warning.${warning}`) })}</span>
        </li>
      ))}
    </ul>
  );
}

/**
 * The validation report of an import file (spec: Import screen): what blocks or allows the import
 * first, focused when it appears so it is announced and in view; then the figures, the ignored
 * columns, and the rows with errors or warnings, which can be narrowed to the rows with errors.
 * Render it with a new `key` per report, so the outcome is focused and the filter reset each time.
 */
export function ImportReport({ report, changed = false }: ImportReportProps) {
  const { t } = useTranslation(['registry', 'ui']);
  const { number } = useFormatters();
  const [onlyErrors, setOnlyErrors] = useState(false);
  const hasErrors = report.errorRowCount > 0;
  const canNarrow = hasErrors && report.errorRowCount < report.rows.length;
  const rows = useMemo(
    () => (canNarrow && onlyErrors ? report.rows.filter((row) => row.errors.length > 0) : report.rows),
    [canNarrow, onlyErrors, report.rows],
  );
  const columns = useMemo<DataTableColumn<ArquebusierImportRow>[]>(
    () => [
      {
        id: 'row',
        header: t('import.report.row'),
        cell: (row) => number(row.rowNumber),
        sortValue: (row) => row.rowNumber,
        align: 'end',
      },
      {
        id: 'name',
        header: t('import.report.name'),
        cell: (row) => rowName(t, row),
        sortValue: (row) => rowName(t, row),
        rowHeader: true,
      },
      {
        id: 'problems',
        header: t('import.report.problems'),
        cell: (row) => <Problems row={row} />,
      },
    ],
    [t, number],
  );

  let outcome: string;
  if (changed) {
    outcome = t('import.report.changed', { count: report.errorRowCount });
  } else if (hasErrors) {
    outcome = t('import.report.blocked', { count: report.errorRowCount });
  } else {
    outcome = t('import.report.ready', { count: report.validCount });
  }

  return (
    <div className="flex flex-col gap-group">
      <AlertBanner severity={hasErrors ? 'error' : 'success'} focusOnMount>
        {outcome}
      </AlertBanner>
      <div
        role="group"
        aria-label={t('import.report.title')}
        className="grid grid-cols-2 gap-3 md:grid-cols-4"
      >
        <StatCard label={t('import.report.rows')} value={number(report.rowCount)} />
        <StatCard label={t('import.report.valid')} value={number(report.validCount)} />
        <StatCard label={t('import.report.errors')} value={number(report.errorRowCount)} />
        <StatCard label={t('import.report.warnings')} value={number(report.warningRowCount)} />
      </div>
      {report.ignoredColumns.length > 0 && (
        <AlertBanner severity="info" live={false}>
          {t('import.report.ignoredColumns', { columns: report.ignoredColumns.join(', ') })}
        </AlertBanner>
      )}
      <AlertBanner severity="info" live={false}>
        {t('import.report.noPhotos')}
      </AlertBanner>
      {report.rows.length === 0 ? (
        <p className="text-body text-muted-foreground">{t('import.report.none')}</p>
      ) : (
        <>
          {canNarrow && (
            <div className="flex flex-col gap-1">
              <CheckboxField
                label={t('import.report.onlyErrors')}
                checked={onlyErrors}
                onCheckedChange={setOnlyErrors}
              />
              {/* Always present, so the new count is announced when the filter changes. */}
              <p role="status" className="sr-only">
                {onlyErrors
                  ? t('import.report.shown', { shown: rows.length, total: report.rows.length })
                  : ''}
              </p>
            </div>
          )}
          <DataTable
            caption={t('import.report.caption')}
            data={rows}
            columns={columns}
            getRowId={(row) => String(row.rowNumber)}
            mobileRow={(row) => (
              <div className="flex flex-col gap-1">
                <span className="font-medium">
                  {t('import.report.row')} {number(row.rowNumber)} · {rowName(t, row)}
                </span>
                <Problems row={row} />
              </div>
            )}
          />
        </>
      )}
    </div>
  );
}
