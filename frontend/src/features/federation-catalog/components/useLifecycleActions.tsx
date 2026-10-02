import { useQueryClient, type QueryKey } from '@tanstack/react-query';
import { CirclePause, CirclePlay, Trash2 } from 'lucide-react';
import { useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { explainFailure } from '@/components/app/confirm-failure';
import type { MoreAction } from '@/components/app/RecordHeader';
import { useSaveNotice } from '@/components/app/save-notice';
import { noticeState, type Announce } from '@/lib/notices';

/** Already translated texts of a catalogue record's lifecycle. */
export interface LifecycleTexts {
  deactivate: string;
  deactivateTitle: string;
  deactivateDescription: string;
  deactivated: string;
  reactivate: string;
  reactivated: string;
  delete: string;
  deleteTitle: string;
  deleteDescription: string;
  deleted: string;
}

export interface LifecycleOptions {
  active: boolean;
  texts: LifecycleTexts;
  deactivate: () => Promise<unknown>;
  reactivate: () => Promise<unknown>;
  remove: () => Promise<unknown>;
  /** Refreshes the record and its list after a change. */
  refresh: () => Promise<void>;
  /** Shows a failure that happened outside a dialog (reactivating). */
  announce: Announce;
  /** The translated reason of a failure. */
  explain: (error: unknown) => string;
  /** Where a deletion leads, with its notice. */
  listPath: string;
  listKey: QueryKey;
  /** The record's queries, dropped once its page is left after a deletion. */
  recordKeys: readonly QueryKey[];
}

/**
 * Deactivate (or reactivate) and delete a catalogue record from "More actions" (spec: Action
 * hierarchy): confirmations return focus to "More actions", a success is announced without moving
 * focus, a failure stays in its dialog, and a deletion leads back to the list with its notice.
 */
export function useLifecycleActions(options: LifecycleOptions) {
  const { active, texts, refresh, announce, explain } = options;
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const notify = useSaveNotice();
  const moreActions = useRef<HTMLButtonElement>(null);
  const [dialog, setDialog] = useState<'deactivate' | 'delete'>();
  const [reactivating, setReactivating] = useState(false);

  const reactivateNow = async (): Promise<void> => {
    setReactivating(true);
    try {
      await options.reactivate();
    } catch (error) {
      announce('error', explain(error));
      return;
    } finally {
      setReactivating(false);
    }
    notify(texts.reactivated);
    await refresh();
  };

  const afterDelete = (): void => {
    // Leave the page first, then drop the record's queries: nothing refetches them into a 404.
    void Promise.resolve(navigate(options.listPath, { state: noticeState(texts.deleted) })).then(() => {
      for (const queryKey of options.recordKeys) queryClient.removeQueries({ queryKey });
    });
    void queryClient.invalidateQueries({ queryKey: options.listKey });
  };

  const items: MoreAction[] = [
    active
      ? {
          id: 'deactivate',
          label: texts.deactivate,
          icon: CirclePause,
          onSelect: () => {
            setDialog('deactivate');
          },
        }
      : {
          id: 'reactivate',
          label: texts.reactivate,
          icon: CirclePlay,
          // One request at a time.
          disabled: reactivating,
          onSelect: () => {
            void reactivateNow();
          },
        },
    {
      id: 'delete',
      label: texts.delete,
      icon: Trash2,
      destructive: true,
      onSelect: () => {
        setDialog('delete');
      },
    },
  ];

  const close = (open: boolean) => {
    if (!open) setDialog(undefined);
  };

  const dialogs = (
    <>
      <ConfirmDialog
        open={dialog === 'deactivate'}
        onOpenChange={close}
        returnFocus={moreActions}
        tone="primary"
        title={texts.deactivateTitle}
        description={texts.deactivateDescription}
        confirmLabel={texts.deactivate}
        onConfirm={() => explainFailure(options.deactivate, explain)}
        onConfirmed={() => {
          moreActions.current?.focus();
          notify(texts.deactivated);
          void refresh();
        }}
      />
      <ConfirmDialog
        open={dialog === 'delete'}
        onOpenChange={close}
        returnFocus={moreActions}
        title={texts.deleteTitle}
        description={texts.deleteDescription}
        confirmLabel={texts.delete}
        onConfirm={() => explainFailure(options.remove, explain)}
        onConfirmed={afterDelete}
      />
    </>
  );

  return { items, dialogs, moreActions };
}
