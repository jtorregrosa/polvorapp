import { ImageOff } from 'lucide-react';
import { useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import type { ArquebusierRowResponse } from '@/api/generated/model';
import type { DataTableColumn } from '@/components/app/DataTable';
import { StatusBadge } from '@/components/app/StatusBadge';
import { useFormatters } from '@/lib/format';
import { licenseBadgeValue } from '../license-badge';

/** Expiry dates in rows: day, month and year in digits, e.g. 10/03/2030. */
const SHORT_DATE: Intl.DateTimeFormatOptions = { day: '2-digit', month: '2-digit', year: 'numeric' };

/**
 * The arquebusier list as a table (two-line cells: the identity number and a missing ID photo
 * under the name, the expiry under the license) and as stacked items on phones, which say the same
 * (spec: Registry screens, Data tables).
 */
export function useArquebusierColumns() {
  const { t } = useTranslation(['registry', 'ui']);
  const { date, list } = useFormatters();
  const licenseBadge = useCallback(
    (row: ArquebusierRowResponse) =>
      row.licenseStatus ? (
        <StatusBadge kind="license" value={licenseBadgeValue(row.licenseStatus, row.warnings)} />
      ) : null,
    [],
  );
  /** The warning names as a list in words ("A, B y C"), so screen readers pause between them. */
  const warningNames = useCallback(
    (row: ArquebusierRowResponse) => list(row.warnings.map((code) => t(`ui:status.warning.${code}`))),
    [t, list],
  );
  const expiry = useCallback(
    (row: ArquebusierRowResponse) =>
      row.licenseExpiresOn
        ? t('arquebusiers.columns.expiresOn', {
            date: date(new Date(`${row.licenseExpiresOn}T12:00:00Z`), SHORT_DATE),
          })
        : undefined,
    [t, date],
  );

  const columns = useMemo<DataTableColumn<ArquebusierRowResponse>[]>(
    () => [
      {
        id: 'name',
        header: t('arquebusiers.columns.name'),
        sortValue: (row) => `${row.lastName} ${row.firstName}`,
        cell: (row) => (
          <Link to={`/arquebusiers/${row.id}`} className="font-semibold text-foreground hover:underline">
            {`${row.lastName}, ${row.firstName}`}
          </Link>
        ),
        secondary: (row) => (
          <span className="flex flex-wrap items-center gap-x-2">
            <span className="font-mono text-id">{row.nationalId}</span>
            {/* Spec "Photo screens": a missing ID photo is said in words, never by colour or icon alone. */}
            {!row.hasIdPhoto && (
              <span className="inline-flex items-center gap-1">
                <ImageOff aria-hidden="true" className="size-3.5" />
                {t('arquebusiers.columns.noIdPhoto')}
              </span>
            )}
          </span>
        ),
      },
      {
        id: 'comparsa',
        header: t('arquebusiers.columns.comparsa'),
        sortValue: (row) => row.comparsaName,
        cell: (row) => (
          <Link to={`/comparsas/${row.comparsaId}`} className="text-foreground hover:underline">
            {row.comparsaName}
          </Link>
        ),
      },
      {
        id: 'status',
        header: t('arquebusiers.columns.status'),
        cell: (row) => <StatusBadge kind="arquebusier" value={row.status} />,
      },
      {
        id: 'license',
        header: t('arquebusiers.columns.license'),
        sortValue: (row) => row.licenseExpiresOn ?? '',
        cell: (row) =>
          licenseBadge(row) ?? <span className="text-muted-foreground">{t('arquebusiers.noLicense')}</span>,
        secondary: expiry,
      },
      {
        id: 'warnings',
        header: t('arquebusiers.columns.warnings'),
        sortValue: (row) => row.warnings.length,
        cell: (row) =>
          row.warnings.length > 0 ? (
            t('arquebusiers.warningCount', { count: row.warnings.length })
          ) : (
            <span className="text-muted-foreground">{t('arquebusiers.noWarnings')}</span>
          ),
        // Up to eight names: the line wraps instead of widening the table.
        secondary: (row) =>
          row.warnings.length > 0 ? (
            <span className="block max-w-64 whitespace-normal">{warningNames(row)}</span>
          ) : undefined,
      },
      {
        id: 'federationId',
        header: t('arquebusiers.columns.federationId'),
        align: 'end',
        sortValue: (row) => row.federationId,
        cell: (row) => <span className="font-mono text-id">{row.federationId}</span>,
      },
    ],
    [t, licenseBadge, warningNames, expiry],
  );

  const mobileRow = useCallback(
    (row: ArquebusierRowResponse) => (
      <>
        <Link to={`/arquebusiers/${row.id}`} className="font-semibold text-foreground">
          {`${row.lastName}, ${row.firstName}`}
        </Link>
        <span className="text-help text-muted-foreground">
          <span className="font-mono">{row.nationalId}</span>
          {` · ${row.comparsaName}`}
        </span>
        <span className="flex flex-wrap items-center gap-2">
          {/* Outside the table there is no column header: the badge is named "License: …". */}
          <span className="sr-only">{t('arquebusiers.columns.license')}: </span>
          {licenseBadge(row) ?? (
            <span className="text-help text-muted-foreground">{t('arquebusiers.noLicense')}</span>
          )}
          {row.licenseExpiresOn && <span className="text-help text-muted-foreground">{expiry(row)}</span>}
          {!row.hasIdPhoto && (
            <span className="text-help text-muted-foreground">{t('arquebusiers.columns.noIdPhoto')}</span>
          )}
        </span>
        {row.warnings.length > 0 && (
          <span className="text-help text-muted-foreground">
            {t('arquebusiers.warningSummary', { count: row.warnings.length, names: warningNames(row) })}
          </span>
        )}
      </>
    ),
    [t, licenseBadge, warningNames, expiry],
  );

  return { columns, mobileRow };
}
