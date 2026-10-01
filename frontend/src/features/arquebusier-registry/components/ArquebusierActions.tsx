import { useQueryClient } from '@tanstack/react-query';
import { ArrowRightLeft, Trash2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import {
  getGetArquebusierQueryKey,
  getListArquebusiersQueryKey,
  useDeleteArquebusier,
  useTransferArquebusier,
} from '@/api/generated/arquebusiers/arquebusiers';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import type { ArquebusierResponse, ComparsaResponse } from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { ConfirmFailure } from '@/components/app/confirm-failure';
import { FilterSelect } from '@/components/app/FilterSelect';
import { PageSection } from '@/components/app/PageSection';
import { useSession } from '@/features/identity-access/session';
import { noticeState, type Announce } from '@/lib/notices';
import { problemCode, problemMessage } from '../problems';

const fullName = (arquebusier: ArquebusierResponse): string =>
  `${arquebusier.firstName} ${arquebusier.lastName}`;

/**
 * Spec "Transfer between comparsas (UC-29, BR-13)": an Admin moves the arquebusier, with their owned
 * weapons, to another active comparsa, after a confirmation that names both comparsas.
 */
function TransferPanel({ arquebusier, announce, onChanged }: ArquebusierActionsProps) {
  const { t } = useTranslation('registry');
  const transfer = useTransferArquebusier();
  const [target, setTarget] = useState('');
  // Without includeInactive the API lists only active comparsas.
  const comparsas = useListComparsas();
  const targets = useMemo(
    () =>
      ((comparsas.data?.data ?? []) as ComparsaResponse[]).filter(
        (comparsa) => comparsa.active && comparsa.id !== arquebusier.comparsaId,
      ),
    [arquebusier.comparsaId, comparsas.data],
  );
  // A chosen comparsa that stopped being a target (deactivated meanwhile) is no longer chosen.
  const chosen = targets.find((comparsa) => comparsa.id === target);
  const to = chosen?.name ?? '';
  const name = fullName(arquebusier);

  return (
    <PageSection title={t('transfer.title')} description={t('transfer.description')}>
      {comparsas.isError && <AlertBanner severity="error">{problemMessage(t, comparsas.error)}</AlertBanner>}
      {comparsas.isSuccess && targets.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('transfer.noTargets')}</p>
      ) : (
        <div className="flex flex-wrap items-end gap-4">
          {!chosen && <p className="w-full text-sm text-muted-foreground">{t('transfer.chooseHint')}</p>}
          <FilterSelect
            label={t('transfer.target')}
            value={chosen ? target : ''}
            onChange={setTarget}
            options={[
              { value: '', label: t('validation.choice') },
              ...targets.map((comparsa) => ({ value: comparsa.id, label: comparsa.name })),
            ]}
          />
          <ConfirmDialog
            title={t('transfer.confirmTitle', { name, to })}
            description={t('transfer.confirmDescription', { from: arquebusier.comparsaName, to })}
            confirmLabel={t('transfer.confirm')}
            onConfirm={async () => {
              try {
                await transfer.mutateAsync({ id: arquebusier.id, data: { comparsaId: chosen?.id ?? '' } });
              } catch (error) {
                throw new ConfirmFailure(problemMessage(t, error, { isAdmin: true }));
              }
            }}
            onConfirmed={() => {
              announce('success', t('transfer.done', { name, to }));
              setTarget('');
              void onChanged();
            }}
            trigger={
              <Button type="button" variant="secondary" icon={ArrowRightLeft} disabled={!chosen}>
                {t('transfer.action')}
              </Button>
            }
          />
        </div>
      )}
    </PageSection>
  );
}

/**
 * Spec "Deleting an arquebusier (UC-05, BR-14)": deletes the arquebusier who left the Federation,
 * after a confirmation that names them, says it cannot be undone and suggests Reserve instead.
 */
function DeletePanel({ arquebusier }: Pick<ArquebusierActionsProps, 'arquebusier'>) {
  const { t } = useTranslation('registry');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const remove = useDeleteArquebusier();
  const name = fullName(arquebusier);

  return (
    <PageSection title={t('delete.title')} description={t('delete.description')}>
      <div>
        <ConfirmDialog
          title={t('delete.confirmTitle', { name })}
          description={`${t('delete.confirmDescription')} ${t('delete.reserveHint')}`}
          confirmLabel={t('delete.confirm')}
          onConfirm={async () => {
            try {
              await remove.mutateAsync({ id: arquebusier.id });
            } catch (error) {
              // Already deleted by someone else: the outcome the user asked for.
              if (problemCode(error) !== 'arquebusiers.notFound') {
                throw new ConfirmFailure(problemMessage(t, error));
              }
            }
          }}
          onConfirmed={() => {
            void queryClient.invalidateQueries({ queryKey: getListArquebusiersQueryKey() });
            // Leave the page first, then drop the deleted person's data: nothing refetches it into a 404.
            void Promise.resolve(
              navigate('/arquebusiers', { state: noticeState(t('delete.deleted', { name })) }),
            ).then(() => {
              queryClient.removeQueries({ queryKey: getGetArquebusierQueryKey(arquebusier.id) });
            });
          }}
          trigger={
            <Button type="button" variant="destructive" icon={Trash2}>
              {t('delete.action')}
            </Button>
          }
        />
      </div>
    </PageSection>
  );
}

export interface ArquebusierActionsProps {
  arquebusier: ArquebusierResponse;
  announce: Announce;
  onChanged: () => Promise<void>;
}

/** The detail page's actions: transfer (Admins only) and deletion. */
export function ArquebusierActions(props: ArquebusierActionsProps) {
  const isAdmin = useSession().account?.role === 'ADMIN';
  return (
    <>
      {isAdmin && <TransferPanel {...props} />}
      <DeletePanel arquebusier={props.arquebusier} />
    </>
  );
}
