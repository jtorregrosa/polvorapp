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
import { CategoryTag } from '@/components/app/Tag';
import { yesNo } from '@/components/app/tags';
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
  // Every model by default; "only active" is the API's own default, so it sends no flag (D8).
  const onlyActive = search.get('onlyActive') === 'true';
  const params: ListWeaponModelsParams = {
    ...(kind ? { kind } : {}),
    ...(onlyActive ? {} : { includeInactive: true }),
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
    return [
      {
        id: 'label',
        header: t('weaponModels.columns.label'),
        rowHeader: true,
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
        sortValue: (model) => tUi(`tag.weaponKind.${model.kind}`),
        cell: (model) => <CategoryTag category="weaponKind" value={model.kind} />,
      },
      {
        id: 'side',
        header: t('weaponModels.columns.side'),
        sortValue: (model) => (model.side ? tUi(`tag.side.${model.side}`) : ''),
        cell: (model) => (model.side ? <CategoryTag category="side" value={model.side} /> : none),
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
        sortValue: (model) => tUi(`tag.yesNo.${yesNo(model.rentable)}`),
        cell: (model) => <CategoryTag category="yesNo" value={yesNo(model.rentable)} />,
      },
      {
        id: 'status',
        header: t('weaponModels.columns.status'),
        sortValue: (model) => tUi(`status.catalog.${model.active ? 'ACTIVE' : 'INACTIVE'}`),
        cell: (model) => <StatusBadge kind="catalog" value={model.active ? 'ACTIVE' : 'INACTIVE'} />,
      },
    ];
  }, [t, tUi]);

  const setFilter = (key: 'kind' | 'onlyActive', value: string): void => {
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
                label={t('weaponModels.filters.onlyActive')}
                checked={onlyActive}
                onCheckedChange={(checked) => {
                  setFilter('onlyActive', checked ? 'true' : '');
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
                {/* Each value with its term for screen readers (WCAG 1.3.1). */}
                <span>
                  <span className="sr-only">{t('weaponModels.columns.kind')}: </span>
                  <CategoryTag category="weaponKind" value={model.kind} />
                </span>
                {model.side && (
                  <span>
                    <span className="sr-only">{t('weaponModels.columns.side')}: </span>
                    <CategoryTag category="side" value={model.side} />
                  </span>
                )}
                <span>
                  <span className="sr-only">{t('weaponModels.columns.status')}: </span>
                  <StatusBadge kind="catalog" value={model.active ? 'ACTIVE' : 'INACTIVE'} />
                </span>
              </span>
              {/* What the table's other columns say, so nothing is lost on a phone (WCAG 1.4.10). */}
              <span className="text-help text-muted-foreground">
                {[
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
