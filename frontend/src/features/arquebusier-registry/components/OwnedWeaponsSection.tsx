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
import { SectionCard } from '@/components/app/SectionCard';
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
  canWrite = true,
}: {
  arquebusier: ArquebusierResponse;
  announce: Announce;
  onChanged: () => Promise<void>;
  /** False while the registry is locked for the caller (BR-10): no add, edit or remove. */
  canWrite?: boolean;
}) {
  const { t } = useTranslation('registry');
  const { t: catalog } = useTranslation('catalog');
  const modelLabel = useModelLabel();
  const { mutateAsync: removeWeapon } = useRemoveOwnedWeapon();

  const { columns, renderActions } = useMemo(() => {
    // Two weapons may share a number, so row actions are named by model and number.
    const names = (weapon: OwnedWeaponResponse) => ({
      model: modelLabel(weapon.model),
      number: weapon.weaponNumber,
    });
    const renderActions = (weapon: OwnedWeaponResponse) => (
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
    );
    const columns: DataTableColumn<OwnedWeaponResponse>[] = [
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
      ...(canWrite
        ? [
            {
              id: 'actions',
              header: t('ownedWeapons.columns.actions'),
              hideHeader: true,
              pinned: true,
              cell: renderActions,
            },
          ]
        : []),
    ];
    return { columns, renderActions };
  }, [announce, arquebusier.id, canWrite, catalog, modelLabel, onChanged, removeWeapon, t]);

  return (
    <SectionCard
      span="full"
      title={t('ownedWeapons.title')}
      description={t('ownedWeapons.description')}
      action={
        canWrite && (
          <Button asChild variant="secondary">
            <Link to={`/arquebusiers/${arquebusier.id}/weapons/new`}>
              <Plus aria-hidden="true" />
              {t('ownedWeapons.add')}
            </Link>
          </Button>
        )
      }
    >
      <DataTable
        caption={t('ownedWeapons.caption', { name: `${arquebusier.firstName} ${arquebusier.lastName}` })}
        data={arquebusier.ownedWeapons}
        columns={columns}
        getRowId={(weapon) => weapon.id}
        mobileRow={(weapon) => (
          <>
            <span className="font-semibold text-foreground">{modelLabel(weapon.model)}</span>
            <span className="text-help text-muted-foreground">
              {catalog(`kind.${weapon.model.kind}`)} ·{' '}
              <span className="font-mono">{weapon.weaponNumber}</span> ·{' '}
              <span className="font-mono">{weapon.ownershipGuideNumber}</span>
            </span>
            {canWrite && renderActions(weapon)}
          </>
        )}
        paginated={false}
        emptyText={t('ownedWeapons.empty')}
      />
    </SectionCard>
  );
}
