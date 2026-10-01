import { Plus } from 'lucide-react';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useRemoveOwnedWeapon } from '@/api/generated/arquebusiers/arquebusiers';
import type { ArquebusierResponse, OwnedWeaponResponse } from '@/api/generated/model';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { ConfirmFailure } from '@/components/app/confirm-failure';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { PageSection } from '@/components/app/PageSection';
import type { Announce } from '@/lib/notices';
import { useModelLabel } from '../hooks';
import { problemCode, problemMessage } from '../problems';

/**
 * Spec "Owned weapons (UC-04)": the arquebusier's owned weapons with their catalogue model, each
 * editable and removable after a confirmation; the outcome is announced by the page's notice.
 */
export function OwnedWeaponsSection({
  arquebusier,
  announce,
  onChanged,
}: {
  arquebusier: ArquebusierResponse;
  announce: Announce;
  onChanged: () => Promise<void>;
}) {
  const { t } = useTranslation('registry');
  const { t: catalog } = useTranslation('catalog');
  const modelLabel = useModelLabel();
  const { mutateAsync: removeWeapon } = useRemoveOwnedWeapon();

  const columns = useMemo<DataTableColumn<OwnedWeaponResponse>[]>(() => {
    // Two weapons may share a number, so row actions are named by model and number.
    const names = (weapon: OwnedWeaponResponse) => ({
      model: modelLabel(weapon.model),
      number: weapon.weaponNumber,
    });
    return [
      { id: 'model', header: t('ownedWeapons.columns.model'), cell: (weapon) => modelLabel(weapon.model) },
      {
        id: 'kind',
        header: t('ownedWeapons.columns.kind'),
        cell: (weapon) => catalog(`kind.${weapon.model.kind}`),
      },
      {
        id: 'attributes',
        header: t('ownedWeapons.columns.attributes'),
        cell: ({ model }) =>
          [
            model.side && catalog(`side.${model.side}`),
            model.handedness && catalog(`handedness.${model.handedness}`),
            model.size && catalog(`size.${model.size}`),
          ]
            .filter(Boolean)
            .join(' · ') || '—',
      },
      {
        id: 'weaponNumber',
        header: t('ownedWeapons.columns.weaponNumber'),
        cell: (weapon) => weapon.weaponNumber,
      },
      { id: 'guide', header: t('ownedWeapons.columns.guide'), cell: (weapon) => weapon.ownershipGuideNumber },
      {
        id: 'actions',
        header: t('ownedWeapons.columns.actions'),
        hideHeader: true,
        cell: (weapon) => (
          <div className="flex flex-wrap gap-2">
            <Button asChild variant="quiet" size="sm">
              <Link
                to={`/arquebusiers/${arquebusier.id}/weapons/${weapon.id}`}
                aria-label={t('ownedWeapons.editLabel', names(weapon))}
              >
                {t('ownedWeapons.edit')}
              </Link>
            </Button>
            <ConfirmDialog
              title={t('ownedWeapons.removeTitle', { number: weapon.weaponNumber })}
              description={t('ownedWeapons.removeDescription')}
              confirmLabel={t('ownedWeapons.remove')}
              onConfirm={async () => {
                try {
                  await removeWeapon({ id: arquebusier.id, weaponId: weapon.id });
                } catch (error) {
                  // Already removed by someone else: the outcome the user asked for.
                  if (problemCode(error) !== 'ownedWeapons.notFound') {
                    throw new ConfirmFailure(problemMessage(t, error));
                  }
                }
              }}
              onConfirmed={() => {
                announce('success', t('ownedWeapons.removed', { number: weapon.weaponNumber }));
                void onChanged();
              }}
              trigger={
                <Button
                  type="button"
                  variant="quiet"
                  size="sm"
                  aria-label={t('ownedWeapons.removeLabel', names(weapon))}
                >
                  {t('ownedWeapons.remove')}
                </Button>
              }
            />
          </div>
        ),
      },
    ];
  }, [announce, arquebusier.id, catalog, modelLabel, onChanged, removeWeapon, t]);

  return (
    <PageSection
      title={t('ownedWeapons.title')}
      description={t('ownedWeapons.description')}
      actions={
        <Button asChild variant="secondary">
          <Link to={`/arquebusiers/${arquebusier.id}/weapons/new`}>
            <Plus aria-hidden="true" />
            {t('ownedWeapons.add')}
          </Link>
        </Button>
      }
    >
      <DataTable
        caption={t('ownedWeapons.caption', { name: `${arquebusier.firstName} ${arquebusier.lastName}` })}
        data={arquebusier.ownedWeapons}
        columns={columns}
        getRowId={(weapon) => weapon.id}
        paginated={false}
        emptyText={t('ownedWeapons.empty')}
      />
    </PageSection>
  );
}
