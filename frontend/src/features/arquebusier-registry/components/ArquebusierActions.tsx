import { useQueryClient } from '@tanstack/react-query';
import { type RefObject, useEffect, useMemo, useRef, useState } from 'react';
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
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { ConfirmFailure } from '@/components/app/confirm-failure';
import { FilterSelect } from '@/components/app/FilterSelect';
import { noticeState, type Announce } from '@/lib/notices';
import { invalidateOrders } from '@/features/comparsa-orders/queries';
import { problemCode, problemMessage } from '../problems';

const fullName = (arquebusier: ArquebusierResponse): string =>
  `${arquebusier.firstName} ${arquebusier.lastName}`;

export interface ArquebusierDialogProps {
  arquebusier: ArquebusierResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Where focus returns when the dialog closes without an outcome to show. */
  returnFocus: RefObject<HTMLElement | null>;
}

/**
 * Spec "Transfer between comparsas (UC-29, BR-13)": an Admin moves the arquebusier, with their owned
 * weapons, to another active comparsa, after a confirmation that names both comparsas.
 */
export function TransferDialog({
  arquebusier,
  open,
  onOpenChange,
  returnFocus,
  announce,
  onChanged,
}: ArquebusierDialogProps & { announce: Announce; onChanged: () => Promise<void> }) {
  const { t } = useTranslation('registry');
  const transfer = useTransferArquebusier();
  const [target, setTarget] = useState('');
  /** Confirmed without a destination: the select says so. */
  const [missing, setMissing] = useState(false);
  const select = useRef<HTMLSelectElement>(null);
  /** The destination of a confirmed transfer, for its outcome once the dialog has closed. */
  const transferredTo = useRef('');
  // Without includeInactive the API lists only active comparsas.
  const comparsas = useListComparsas(undefined, { query: { enabled: open } });
  const targets = useMemo(
    () =>
      ((comparsas.data?.data ?? []) as ComparsaResponse[]).filter(
        (comparsa) => comparsa.active && comparsa.id !== arquebusier.comparsaId,
      ),
    [arquebusier.comparsaId, comparsas.data],
  );
  // A chosen comparsa that stopped being a target (deactivated meanwhile) is no longer chosen.
  const chosen = targets.find((comparsa) => comparsa.id === target);
  const name = fullName(arquebusier);
  const to = chosen?.name ?? '';

  return (
    <ConfirmDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          setTarget('');
          setMissing(false);
        }
        onOpenChange(next);
      }}
      returnFocus={returnFocus}
      initialFocus={select}
      tone="primary"
      // Once a destination is chosen, the confirmation names both comparsas (spec).
      title={chosen ? t('transfer.confirmTitle', { name, to }) : t('transfer.title')}
      description={
        chosen
          ? t('transfer.confirmDescription', { from: arquebusier.comparsaName, to })
          : t('transfer.description')
      }
      confirmLabel={t('transfer.confirm')}
      onConfirm={async () => {
        if (!chosen) {
          setMissing(true);
          select.current?.focus();
          return false;
        }
        transferredTo.current = chosen.name;
        try {
          await transfer.mutateAsync({ id: arquebusier.id, data: { comparsaId: chosen.id } });
        } catch (error) {
          throw new ConfirmFailure(problemMessage(t, error, { isAdmin: true }));
        }
      }}
      onConfirmed={() => {
        announce('success', t('transfer.done', { name, to: transferredTo.current }));
        setTarget('');
        void onChanged();
      }}
    >
      {comparsas.isError && <AlertBanner severity="error">{problemMessage(t, comparsas.error)}</AlertBanner>}
      {comparsas.isSuccess && targets.length === 0 ? (
        <p className="text-body text-muted-foreground">{t('transfer.noTargets')}</p>
      ) : (
        <FilterSelect
          ref={select}
          label={t('transfer.target')}
          value={chosen ? target : ''}
          error={missing && !chosen ? t('transfer.chooseHint') : undefined}
          onChange={setTarget}
          placeholder={t('validation.choice')}
          options={targets.map((comparsa) => ({ value: comparsa.id, label: comparsa.name }))}
        />
      )}
      {/* The title and description now name both comparsas: say it, focus stays on the select. */}
      <p role="status" className="sr-only">
        {chosen ? t('transfer.confirmDescription', { from: arquebusier.comparsaName, to }) : ''}
      </p>
    </ConfirmDialog>
  );
}

/**
 * Spec "Deleting an arquebusier (UC-05, BR-14)": deletes the arquebusier who left the Federation,
 * after a confirmation that names them, says it cannot be undone and suggests Reserve instead.
 */
export function DeleteDialog({ arquebusier, open, onOpenChange, returnFocus }: ArquebusierDialogProps) {
  const { t } = useTranslation('registry');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const remove = useDeleteArquebusier();
  const name = fullName(arquebusier);

  // What the deletion does to the orders may have changed since the page loaded (orders closed).
  useEffect(() => {
    if (open) void queryClient.invalidateQueries({ queryKey: getGetArquebusierQueryKey(arquebusier.id) });
  }, [open, queryClient, arquebusier.id]);

  return (
    <ConfirmDialog
      open={open}
      onOpenChange={onOpenChange}
      returnFocus={returnFocus}
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
        // The orders show the arquebusier, and the statistics count them (invalidateOrders does both).
        void invalidateOrders(queryClient);
        // Leave the page first, then drop the deleted person's data: nothing refetches it into a 404.
        void Promise.resolve(
          navigate('/arquebusiers', { state: noticeState(t('delete.deleted', { name })) }),
        ).then(() => {
          queryClient.removeQueries({ queryKey: getGetArquebusierQueryKey(arquebusier.id) });
        });
      }}
      notes={<DeletionImpactNotes arquebusier={arquebusier} />}
    />
  );
}

/**
 * What the deletion does to the orders (add-comparsa-orders): the entry of the edition in progress
 * is deleted while its orders are open, or kept as history; lent weapons show as removed; past
 * entries are kept. Nothing when the deletion touches no order.
 */
function DeletionImpactNotes({ arquebusier }: { arquebusier: ArquebusierResponse }) {
  const { t } = useTranslation('registry');
  const { currentEntry, lentWeapons, hasPastEntries } = arquebusier.deletionImpact;
  return (
    <>
      {currentEntry && (
        <AlertBanner severity={currentEntry.willBeRemoved ? 'warning' : 'info'} live={false}>
          {currentEntry.willBeRemoved
            ? t('delete.currentEntry', {
                year: currentEntry.editionYear,
                comparsa: currentEntry.comparsaName,
                status: t(`delete.orderStatus.${currentEntry.orderStatus}`),
              })
            : t('delete.currentEntryKept', {
                year: currentEntry.editionYear,
                comparsa: currentEntry.comparsaName,
              })}
        </AlertBanner>
      )}
      {lentWeapons > 0 && (
        <AlertBanner severity="warning" live={false}>
          {t('delete.lentWeapons', { count: lentWeapons })}
        </AlertBanner>
      )}
      {hasPastEntries && (
        <AlertBanner severity="info" live={false}>
          {t('delete.historyKept')}
        </AlertBanner>
      )}
    </>
  );
}
