import { useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type ReactElement, type ReactNode, type RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import {
  getGetEditionQueryKey,
  getListEditionsQueryKey,
  useChangeEditionStatus,
  useDeleteEdition,
  useSetEditionOrders,
} from '@/api/generated/editions/editions';
import type { EditionResponse } from '@/api/generated/model';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { explainFailure } from '@/components/app/confirm-failure';
import type { MoreAction } from '@/components/app/RecordHeader';
import { useSaveNotice } from '@/components/app/save-notice';
import { noticeState } from '@/lib/notices';
import { ACTION_ICONS, actionsFor, MOVES, type EditionAction } from '../editionActions';
import { problemCode } from '../problems';
import { useEditionRefresh, useExplain } from './useSaveEdition';

export interface EditionActions {
  primary: ReactNode;
  items: MoreAction[];
  moreActionsRef: RefObject<HTMLButtonElement | null>;
  dialogs: ReactNode;
}

/**
 * The edition's lifecycle and orders actions, each confirmed in a dialog that says what changes for
 * FiringChiefs. A failure stays in its dialog with its reason; a success is announced without moving
 * focus, and a deletion leads back to the list with its notice.
 */
export function useEditionActions(edition: EditionResponse): EditionActions {
  const { t } = useTranslation('editions');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const notify = useSaveNotice();
  const explain = useExplain();
  const refresh = useEditionRefresh(edition.id);
  const changeStatus = useChangeEditionStatus();
  const setOrders = useSetEditionOrders();
  const remove = useDeleteEdition();
  const moreActionsRef = useRef<HTMLButtonElement>(null);
  const [open, setOpen] = useState<EditionAction>();
  const { primary, more } = actionsFor(edition);
  const year = edition.year;
  const blockedByOrders = edition.status === 'IN_PROGRESS' && edition.ordersOpen;

  const run = (action: EditionAction): Promise<unknown> => {
    const id = edition.id;
    const version = edition.version;
    if (action === 'openOrders' || action === 'closeOrders') {
      return setOrders.mutateAsync({ id, data: { open: action === 'openOrders', version } });
    }
    if (action === 'delete') {
      return remove.mutateAsync({ id });
    }
    const status = MOVES[action];
    if (!status) throw new Error(`No status move for ${action}.`);
    return changeStatus.mutateAsync({ id, data: { status, version } });
  };

  /** A refusal because the edition changed meanwhile reloads it, so a retry uses its new version. */
  const explainAndReload = (error: unknown): string => {
    const code = problemCode(error);
    if (
      code === 'editions.modified' ||
      code === 'editions.notFound' ||
      code === 'editions.invalidTransition'
    ) {
      void refresh();
    }
    return explain(error);
  };

  const confirmed = (action: EditionAction): void => {
    if (action === 'delete') {
      void Promise.resolve(navigate('/editions', { state: noticeState(t('done.delete', { year })) })).then(
        () => {
          queryClient.removeQueries({ queryKey: getGetEditionQueryKey(edition.id) });
        },
      );
      void queryClient.invalidateQueries({ queryKey: getListEditionsQueryKey() });
      return;
    }
    notify(t(`done.${action}`, { year }));
    void refresh();
  };

  const dialog = (action: EditionAction, trigger?: ReactElement): ReactNode => (
    <ConfirmDialog
      key={action}
      {...(trigger
        ? { trigger }
        : {
            open: open === action,
            onOpenChange: (next: boolean) => {
              if (!next) setOpen(undefined);
            },
            returnFocus: moreActionsRef,
          })}
      tone={action === 'delete' ? 'destructive' : 'primary'}
      title={t(`confirm.${action}.title`, { year })}
      description={t(`confirm.${action}.description`)}
      confirmLabel={t(`actions.${action}`)}
      onConfirm={() => explainFailure(() => run(action), explainAndReload)}
      onConfirmed={() => {
        // The trigger may be gone with the old state: focus "More actions", present in every state (WCAG 2.4.3).
        moreActionsRef.current?.focus();
        confirmed(action);
      }}
    />
  );

  const items: MoreAction[] = more.map((action) => ({
    id: action,
    label: blockedByOrders
      ? `${t(`actions.${action}`)} (${t('actions.closeOrdersFirst')})`
      : t(`actions.${action}`),
    icon: ACTION_ICONS[action],
    destructive: action === 'delete',
    disabled: blockedByOrders,
    onSelect: () => {
      setOpen(action);
    },
  }));

  const primaryButton =
    primary && dialog(primary, <Button icon={ACTION_ICONS[primary]}>{t(`actions.${primary}`)}</Button>);

  return {
    primary: primaryButton,
    items,
    moreActionsRef,
    dialogs: <>{more.map((action) => dialog(action))}</>,
  };
}
