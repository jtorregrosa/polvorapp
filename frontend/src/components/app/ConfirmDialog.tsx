import { useState, type MouseEvent, type ReactElement } from 'react';
import { useTranslation } from 'react-i18next';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog';
import { AlertBanner } from './AlertBanner';

export interface ConfirmDialogProps {
  /** Names the action and its object, e.g. "Delete Ana Pérez?". */
  title: string;
  /** Consequences, especially when the action cannot be undone. */
  description: string;
  /** The action itself ("Delete arquebusier"), never "OK". */
  confirmLabel: string;
  /** May be asynchronous: the dialog stays open and disabled until it settles, and on failure. */
  onConfirm: () => void | Promise<void>;
  /** The button that opens the dialog; focus returns to it on close. Omit when controlled. */
  trigger?: ReactElement;
  /** Controlled mode, e.g. when opened from a row-actions menu. */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
}

/**
 * Confirmation for destructive or irreversible actions (spec: Confirmation of destructive
 * actions). Focus starts on the cancel option; Escape and cancel change nothing.
 */
export function ConfirmDialog({
  title,
  description,
  confirmLabel,
  onConfirm,
  trigger,
  open: controlledOpen,
  onOpenChange,
}: ConfirmDialogProps) {
  const { t } = useTranslation('ui');
  const [uncontrolledOpen, setUncontrolledOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const [failed, setFailed] = useState(false);
  const open = controlledOpen ?? uncontrolledOpen;

  const setOpen = (next: boolean) => {
    if (pending) return;
    if (next) setFailed(false);
    setUncontrolledOpen(next);
    onOpenChange?.(next);
  };

  const confirm = async (event: MouseEvent) => {
    // Keep the dialog open until the action settles; close it ourselves on success.
    event.preventDefault();
    setPending(true);
    setFailed(false);
    try {
      await onConfirm();
      setPending(false);
      setUncontrolledOpen(false);
      onOpenChange?.(false);
    } catch {
      setPending(false);
      setFailed(true);
    }
  };

  return (
    <AlertDialog open={open} onOpenChange={setOpen}>
      {trigger && <AlertDialogTrigger asChild>{trigger}</AlertDialogTrigger>}
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>
        {failed && <AlertBanner severity="error">{t('confirm.failed')}</AlertBanner>}
        <AlertDialogFooter>
          <AlertDialogCancel disabled={pending}>{t('confirm.cancel')}</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            disabled={pending}
            onClick={(event) => void confirm(event)}
          >
            {confirmLabel}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
