import { Plus } from 'lucide-react';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { WeaponKind, type ListWeaponModelsParams, type WeaponModelResponse } from '@/api/generated/model';
import { useListWeaponModels } from '@/api/generated/weapon-models/weapon-models';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { CheckboxField } from '@/components/app/CheckboxField';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { FilterSelect } from '@/components/app/FilterSelect';
import { PageHeader } from '@/components/app/PageHeader';
import { StatusBadge } from '@/components/app/StatusBadge';
import { knownFilter, withFilter } from '@/lib/search-filters';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { useNotice } from '../notices';
import { problemMessage } from '../problems';

const KINDS = Object.values(WeaponKind);

/** Specs "Weapon models" and "Weapon catalogue access": the catalogue, for Admins. */
export function WeaponModelsPage() {
  const { t } = useTranslation('catalog');
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
      <>
        <span aria-hidden="true">{t('weaponModels.none')}</span>
        <span className="sr-only">{t('weaponModels.form.notSet')}</span>
      </>
    );
    return [
      {
        id: 'label',
        header: t('weaponModels.columns.label'),
        sortValue: (model) => model.label,
        cell: (model) => (
          <Link
            to={`/weapon-models/${model.id}`}
            className="font-medium text-primary underline-offset-4 hover:underline"
          >
            {model.label}
          </Link>
        ),
      },
      { id: 'kind', header: t('weaponModels.columns.kind'), cell: (model) => t(`kind.${model.kind}`) },
      {
        id: 'side',
        header: t('weaponModels.columns.side'),
        cell: (model) => (model.side ? t(`side.${model.side}`) : none),
      },
      {
        id: 'handedness',
        header: t('weaponModels.columns.handedness'),
        cell: (model) => (model.handedness ? t(`handedness.${model.handedness}`) : none),
      },
      {
        id: 'size',
        header: t('weaponModels.columns.size'),
        cell: (model) => (model.size ? t(`size.${model.size}`) : none),
      },
      {
        id: 'rentable',
        header: t('weaponModels.columns.rentable'),
        cell: (model) => (model.rentable ? t('weaponModels.yes') : t('weaponModels.no')),
      },
      {
        id: 'status',
        header: t('weaponModels.columns.status'),
        cell: (model) => <StatusBadge kind="catalog" value={model.active ? 'ACTIVE' : 'INACTIVE'} />,
      },
    ];
  }, [t]);

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
      {notice && (
        <AlertBanner key={notice.id} severity={notice.severity} className="mb-4 max-w-xl" focusOnMount>
          {notice.text}
        </AlertBanner>
      )}
      <div className="mb-4 flex flex-wrap items-end gap-4">
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
        <div className="flex h-9 items-center">
          <CheckboxField
            label={t('weaponModels.filters.includeInactive')}
            checked={includeInactive}
            onCheckedChange={(checked) => {
              setFilter('includeInactive', checked ? 'true' : '');
            }}
          />
        </div>
      </div>
      {models.isError ? (
        <AlertBanner severity="error">{problemMessage(t, models.error)}</AlertBanner>
      ) : (
        <DataTable
          caption={t('weaponModels.caption')}
          data={rows}
          columns={columns}
          getRowId={(model) => model.id}
          isLoading={models.isPending}
          emptyText={t('weaponModels.empty.description')}
        />
      )}
    </>
  );
}
