import { ImageOff } from 'lucide-react';
import { useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import type { ArquebusierRowResponse } from '@/api/generated/model';
import type { DataTableColumn } from '@/components/app/DataTable';
import { StatusBadge } from '@/components/app/StatusBadge';
import { useFormatters } from '@/lib/format';

/** Expiry dates in rows: day, month and year in digits, e.g. 10/03/2030. */
const SHORT_DATE: Intl.DateTimeFormatOptions = { day: '2-digit', month: '2-digit', year: 'numeric' };

/**
 * The arquebusier list as a table (two-line cells: the identity number and a missing ID photo
 * under the name, the expiry under the license) and as stacked items on phones, which say the same
 * (spec: Registry screens, Data tables).
 */
export function useArquebusierColumns() {
  const { t } = useTranslation('registry');
  const { date } = useFormatters();

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
        cell: (row) => row.comparsaName,
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
          row.licenseStatus ? (
            <StatusBadge kind="license" value={row.licenseStatus} />
          ) : (
            <span className="text-muted-foreground">{t('arquebusiers.noLicense')}</span>
          ),
        secondary: (row) =>
          row.licenseExpiresOn
            ? t('arquebusiers.columns.expiresOn', {
                date: date(new Date(`${row.licenseExpiresOn}T12:00:00Z`), SHORT_DATE),
              })
            : undefined,
      },
      {
        id: 'federationId',
        header: t('arquebusiers.columns.federationId'),
        align: 'end',
        sortValue: (row) => row.federationId,
        cell: (row) => <span className="font-mono text-id">{row.federationId}</span>,
      },
    ],
    [t, date],
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
          {row.licenseStatus ? (
            <StatusBadge kind="license" value={row.licenseStatus} />
          ) : (
            <span className="text-help text-muted-foreground">{t('arquebusiers.noLicense')}</span>
          )}
          {!row.hasIdPhoto && (
            <span className="text-help text-muted-foreground">{t('arquebusiers.columns.noIdPhoto')}</span>
          )}
        </span>
      </>
    ),
    [t],
  );

  return { columns, mobileRow };
}
