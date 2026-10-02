import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import {
  ArquebusierStatus,
  ComplianceWarning,
  LicenseStatus,
  type ArquebusierRowResponse,
} from '@/api/generated/model';
import type { StatFilterItem } from '@/components/app/StatFilter';
import { knownFilter, withFilter } from '@/lib/search-filters';
import { matchesSearch, searchKey } from '../search';

const STATUSES = Object.values(ArquebusierStatus);
/**
 * License filters: expiring soon (the compliance warning, as the server derives it), a license
 * state, or no license at all.
 */
const LICENSE_FILTERS = ['EXPIRING', LicenseStatus.EXPIRED, LicenseStatus.PENDING, 'NONE'] as const;
type LicenseFilter = (typeof LICENSE_FILTERS)[number];
/** Warning filters: any compliance warning, or one of them (spec: Arquebusier visibility). */
export const WARNING_FILTERS = ['ANY', ...Object.values(ComplianceWarning)] as const;
type WarningFilter = (typeof WARNING_FILTERS)[number];
type CounterId = 'active' | 'reserve' | 'expiring' | 'expired' | 'pending' | 'none';

function matchesLicense(row: ArquebusierRowResponse, license: LicenseFilter | ''): boolean {
  switch (license) {
    case '':
      return true;
    case 'EXPIRING':
      return row.warnings.includes(ComplianceWarning.LICENSE_EXPIRING);
    case 'NONE':
      return row.licenseStatus === null;
    default:
      return row.licenseStatus === license;
  }
}

function matchesWarning(row: ArquebusierRowResponse, warning: WarningFilter | ''): boolean {
  if (warning === '') return true;
  return warning === 'ANY' ? row.warnings.length > 0 : row.warnings.includes(warning);
}

/** The loaded rows that pass the status, license, warning and search filters (comparsa is the API's). */
export function filterRows(
  rows: readonly ArquebusierRowResponse[],
  {
    status,
    license,
    warning,
    searchTerm,
  }: { status: string; license: LicenseFilter | ''; warning: WarningFilter | ''; searchTerm: string },
): ArquebusierRowResponse[] {
  return rows.filter(
    (row) =>
      (!status || row.status === status) &&
      matchesLicense(row, license) &&
      matchesWarning(row, warning) &&
      (!searchTerm || matchesSearch(row, searchTerm)),
  );
}

/**
 * The filters of the arquebusier list (spec: Registry screens): comparsa, status, license and
 * compliance warning in the address, so they survive a reload and a dashboard figure can link to
 * them; the search only in memory, because it may be a DNI. Knows
 * whether the person has filtered, so the result count is only announced then.
 */
export function useArquebusierFilters(comparsaIds: readonly string[]) {
  const { t } = useTranslation('registry');
  const [search, setSearch] = useSearchParams();
  const [term, setTermState] = useState('');
  // The count is announced once the user has searched or filtered, not on the first load.
  const [filtered, setFiltered] = useState(false);
  const comparsaId = knownFilter(search.get('comparsaId'), comparsaIds);
  const status = knownFilter(search.get('status'), STATUSES);
  const license = knownFilter(search.get('license'), LICENSE_FILTERS);
  const warning = knownFilter(search.get('warning'), WARNING_FILTERS);
  const searchTerm = searchKey(term);

  const setFilter = (key: 'comparsaId' | 'status' | 'license' | 'warning', value: string): void => {
    setFiltered(true);
    setSearch(withFilter(search, key, value), { replace: true });
  };

  const setTerm = (value: string): void => {
    setFiltered(true);
    setTermState(value);
  };

  const clearFilters = (): void => {
    setFiltered(true);
    setTermState('');
    setSearch(new URLSearchParams(), { replace: true });
  };

  /** The counters above the list: each counts the rows in scope and toggles its filter (design D10). */
  const counters = (rows: readonly ArquebusierRowResponse[]): StatFilterItem[] => {
    const counter = (
      id: CounterId,
      test: (row: ArquebusierRowResponse) => boolean,
      key: 'status' | 'license',
      value: string,
      tone?: StatFilterItem['tone'],
    ): StatFilterItem => ({
      id,
      label: t(`arquebusiers.counters.${id}`),
      hint: id === 'expiring' ? t('arquebusiers.counters.expiringHint') : undefined,
      count: rows.filter(test).length,
      tone,
      pressed: (key === 'status' ? status : license) === value,
      onPressedChange: (on) => {
        setFilter(key, on ? value : '');
      },
    });
    return [
      counter('active', (row) => row.status === 'ACTIVE', 'status', 'ACTIVE'),
      counter('reserve', (row) => row.status === 'RESERVE', 'status', 'RESERVE'),
      counter(
        'expiring',
        (row) => row.warnings.includes(ComplianceWarning.LICENSE_EXPIRING),
        'license',
        'EXPIRING',
        'warning',
      ),
      counter('expired', (row) => row.licenseStatus === 'EXPIRED', 'license', 'EXPIRED', 'warning'),
      counter('pending', (row) => row.licenseStatus === 'PENDING', 'license', 'PENDING'),
      counter('none', (row) => row.licenseStatus === null, 'license', 'NONE'),
    ];
  };

  return {
    comparsaId,
    /** A comparsa filter is in the address, known or not yet. */
    comparsaInAddress: search.get('comparsaId') !== null,
    status,
    license,
    warning,
    term,
    searchTerm,
    filtered,
    setFilter,
    setTerm,
    clearFilters,
    counters,
  };
}
