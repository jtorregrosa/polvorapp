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
import { ConfirmFailure } from './confirm-failure';

export interface ConfirmDialogProps {
  /** Names the action and its object, e.g. "Delete Ana Pérez?". */
  title: string;
  /** Consequences, especially when the action cannot be undone. */
  description: string;
  /** The action itself ("Delete arquebusier"), never "OK". */
  confirmLabel: string;
  /**
   * May be asynchronous: the dialog stays open and disabled until it settles, and on failure. A
   * {@link ConfirmFailure} rejection shows its (translated) message; anything else a generic one.
   */
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
  const [failure, setFailure] = useState<string>();
  const open = controlledOpen ?? uncontrolledOpen;

  const setOpen = (next: boolean) => {
    if (pending) return;
    if (next) setFailure(undefined);
    setUncontrolledOpen(next);
    onOpenChange?.(next);
  };

  const confirm = async (event: MouseEvent) => {
    // Keep the dialog open until the action settles; close it ourselves on success.
    event.preventDefault();
    setPending(true);
    setFailure(undefined);
    try {
      await onConfirm();
      setPending(false);
      setUncontrolledOpen(false);
      onOpenChange?.(false);
    } catch (error) {
      setPending(false);
      setFailure(error instanceof ConfirmFailure ? error.message : t('confirm.failed'));
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
        {failure && <AlertBanner severity="error">{failure}</AlertBanner>}
        <AlertDialogFooter>
          <AlertDialogCancel disabled={pending}>{t('confirm.cancel')}</AlertDialogCancel>
          {/* Busy rather than disabled while pending, so focus stays on it (WCAG 2.4.3). */}
          <AlertDialogAction
            variant="destructive"
            aria-disabled={pending || undefined}
            aria-busy={pending || undefined}
            className="aria-disabled:cursor-progress aria-disabled:opacity-50"
            onClick={(event) => {
              if (pending) {
                event.preventDefault();
                return;
              }
              void confirm(event);
            }}
          >
            {confirmLabel}
            {pending && <span className="sr-only">{t('button.pending')}</span>}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
