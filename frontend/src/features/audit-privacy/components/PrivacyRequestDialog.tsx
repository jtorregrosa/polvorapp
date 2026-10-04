import { useRef, type ReactElement, type ReactNode, type RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { ConfirmFailure } from '@/components/app/confirm-failure';
import { privacyFieldError, privacyProblemMessage } from '../privacy-problems';
import { ReferenceField } from './ReferenceField';
import { useReferenceForm } from './reference-form';

export interface PrivacyRequestDialogProps<TResult> {
  /** `primary` for a download, `destructive` for an erasure. */
  tone: 'primary' | 'destructive';
  title: string;
  description: string;
  confirmLabel: string;
  /** What must be heard before confirming, e.g. the erasure's warnings. */
  notes?: ReactNode;
  /** Sends the request with the checked reference and returns its result; rejects with the API's refusal. */
  onRequest: (reference: string) => Promise<TResult>;
  /** Runs with the result once the dialog has closed after the request succeeded, e.g. to announce the outcome. */
  onDone: (result: TResult) => void;
  trigger?: ReactElement;
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  returnFocus?: RefObject<HTMLElement | null>;
}

/**
 * A GDPR export or erasure (spec: GDPR request screens): a confirmation that asks for the request
 * reference. A download starts on the reference; an erasure starts on Cancel, like any destructive
 * confirmation. A refused reference is shown on its field, any other refusal in the dialog.
 */
export function PrivacyRequestDialog<TResult>({
  tone,
  title,
  description,
  confirmLabel,
  notes,
  onRequest,
  onDone,
  trigger,
  open,
  onOpenChange,
  returnFocus,
}: PrivacyRequestDialogProps<TResult>) {
  const { t } = useTranslation('privacy');
  const form = useReferenceForm();
  const reference = useRef<HTMLInputElement | null>(null);
  // Handed to onDone once the dialog has closed, after the render that confirmed.
  const result = useRef<{ value: TResult }>(undefined);

  const confirm = async (): Promise<false | undefined> => {
    if (!(await form.trigger('reference'))) {
      form.setFocus('reference');
      return false;
    }
    try {
      result.current = { value: await onRequest(form.getValues('reference').trim()) };
    } catch (error) {
      const fieldError = privacyFieldError(error, 'reference');
      if (fieldError) {
        form.setError('reference', { type: 'server', message: fieldError });
        form.setFocus('reference');
        return false;
      }
      throw new ConfirmFailure(privacyProblemMessage(t, error));
    }
    return undefined;
  };

  return (
    <ConfirmDialog
      tone={tone}
      title={title}
      description={description}
      confirmLabel={confirmLabel}
      notes={notes}
      trigger={trigger}
      open={open}
      onOpenChange={(next) => {
        if (!next) form.reset();
        onOpenChange?.(next);
      }}
      returnFocus={returnFocus}
      initialFocus={tone === 'primary' ? reference : undefined}
      onConfirm={confirm}
      onConfirmed={() => {
        form.reset();
        const done = result.current;
        result.current = undefined;
        if (done) onDone(done.value);
      }}
    >
      <ReferenceField form={form} inputRef={reference} />
    </ConfirmDialog>
  );
}
