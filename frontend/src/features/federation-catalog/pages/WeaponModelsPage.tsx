import { Plus } from 'lucide-react';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { WeaponKind, type ListWeaponModelsParams, type WeaponModelResponse } from '@/api/generated/model';
import { useListWeaponModels } from '@/api/generated/weapon-models/weapon-models';
import { AlertBanner, NoticeBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { CheckboxField } from '@/components/app/CheckboxField';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { FilterBar } from '@/components/app/FilterBar';
import { FilterSelect } from '@/components/app/FilterSelect';
import { PageHeader } from '@/components/app/PageHeader';
import { StatusBadge } from '@/components/app/StatusBadge';
import { knownFilter, withFilter } from '@/lib/search-filters';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { useNotice } from '@/lib/notices';
import { problemMessage } from '../problems';

const KINDS = Object.values(WeaponKind);

/** Specs "Weapon models" and "Weapon catalogue access": the catalogue, for Admins. */
export function WeaponModelsPage() {
  const { t } = useTranslation('catalog');
  const { t: tUi } = useTranslation('ui');
  useDocumentTitle(t('weaponModels.title'));
  const [notice] = useNotice();
  const [search, setSearch] = useSearchParams();
  const kind = knownFilter(search.get('kind'), KINDS);
  const includeInactive = search.get('includeInactive') === 'true';
  const params: ListWeaponModelsParams = {
    ...(kind ? { kind } : {}),
    ...(includeInactive ? { includeInactive } : {}),
  };
  const models = useListWeaponModels(params);
  const rows = useMemo(() => (models.data?.data ?? []) as WeaponModelResponse[], [models.data]);

  const columns = useMemo<DataTableColumn<WeaponModelResponse>[]>(() => {
    // A dash for sighted users, words for screen readers (a lone dash is often skipped).
    const none = (
      <span className="text-muted-foreground">
        <span aria-hidden="true">{t('weaponModels.none')}</span>
        <span className="sr-only">{t('weaponModels.form.notSet')}</span>
      </span>
    );
    const yesNo = (value: boolean) => (value ? t('weaponModels.yes') : t('weaponModels.no'));
    return [
      {
        id: 'label',
        header: t('weaponModels.columns.label'),
        sortValue: (model) => model.label,
        cell: (model) => (
          <Link to={`/weapon-models/${model.id}`} className="font-semibold text-foreground hover:underline">
            {model.label}
          </Link>
        ),
      },
      {
        id: 'kind',
        header: t('weaponModels.columns.kind'),
        sortValue: (model) => t(`kind.${model.kind}`),
        cell: (model) => t(`kind.${model.kind}`),
      },
      {
        id: 'side',
        header: t('weaponModels.columns.side'),
        sortValue: (model) => (model.side ? t(`side.${model.side}`) : ''),
        cell: (model) => (model.side ? t(`side.${model.side}`) : none),
      },
      {
        id: 'handedness',
        header: t('weaponModels.columns.handedness'),
        sortValue: (model) => (model.handedness ? t(`handedness.${model.handedness}`) : ''),
        cell: (model) => (model.handedness ? t(`handedness.${model.handedness}`) : none),
      },
      {
        id: 'size',
        header: t('weaponModels.columns.size'),
        sortValue: (model) => (model.size ? t(`size.${model.size}`) : ''),
        cell: (model) => (model.size ? t(`size.${model.size}`) : none),
      },
      {
        id: 'rentable',
        header: t('weaponModels.columns.rentable'),
        sortValue: (model) => yesNo(model.rentable),
        cell: (model) => yesNo(model.rentable),
      },
      {
        id: 'status',
        header: t('weaponModels.columns.status'),
        sortValue: (model) => tUi(`status.catalog.${model.active ? 'ACTIVE' : 'INACTIVE'}`),
        cell: (model) => <StatusBadge kind="catalog" value={model.active ? 'ACTIVE' : 'INACTIVE'} />,
      },
    ];
  }, [t, tUi]);

  const setFilter = (key: 'kind' | 'includeInactive', value: string): void => {
    setSearch(withFilter(search, key, value), { replace: true });
  };

  return (
    <>
      <PageHeader
        title={t('weaponModels.title')}
        description={t('weaponModels.description')}
        actions={
          <Button asChild>
            <Link to="/weapon-models/new">
              <Plus aria-hidden="true" />
              {t('weaponModels.new')}
            </Link>
          </Button>
        }
      />
      <NoticeBanner notice={notice} />
      <FilterBar
        resultText={models.isSuccess ? t('weaponModels.resultCount', { count: rows.length }) : ''}
        filters={
          <>
            <FilterSelect
              label={t('weaponModels.filters.kind')}
              value={kind}
              onChange={(value) => {
                setFilter('kind', value);
              }}
              options={[
                { value: '', label: t('weaponModels.filters.all') },
                ...KINDS.map((value) => ({ value, label: t(`kind.${value}`) })),
              ]}
            />
            <div className="flex min-h-control items-center">
              <CheckboxField
                label={t('weaponModels.filters.includeInactive')}
                checked={includeInactive}
                onCheckedChange={(checked) => {
                  setFilter('includeInactive', checked ? 'true' : '');
                }}
              />
            </div>
          </>
        }
      />
      {models.isError ? (
        <AlertBanner severity="error">{problemMessage(t, models.error)}</AlertBanner>
      ) : (
        <DataTable
          caption={t('weaponModels.caption')}
          data={rows}
          columns={columns}
          getRowId={(model) => model.id}
          getRowHref={(model) => `/weapon-models/${model.id}`}
          mobileRow={(model) => (
            <>
              <Link to={`/weapon-models/${model.id}`} className="font-semibold text-foreground">
                {model.label}
              </Link>
              <span className="flex flex-wrap items-center gap-2 text-help text-muted-foreground">
                {t(`kind.${model.kind}`)}
                <StatusBadge kind="catalog" value={model.active ? 'ACTIVE' : 'INACTIVE'} />
              </span>
              {/* What the table's other columns say, so nothing is lost on a phone (WCAG 1.4.10). */}
              <span className="text-help text-muted-foreground">
                {[
                  model.side && t(`side.${model.side}`),
                  model.handedness && t(`handedness.${model.handedness}`),
                  model.size && t(`size.${model.size}`),
                  model.rentable ? t('weaponModels.rentable') : t('weaponModels.notRentable'),
                ]
                  .filter(Boolean)
                  .join(' · ')}
              </span>
            </>
          )}
          isLoading={models.isPending}
          emptyText={t('weaponModels.empty.description')}
        />
      )}
    </>
  );
}
