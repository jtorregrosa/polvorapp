import { Pencil, type LucideIcon } from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode, type Ref } from 'react';
import { useFormState, type FieldValues, type UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Button as ButtonPrimitive } from '@/components/ui/button';
import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from '@/components/ui/sheet';
import { useIsMobile } from '@/hooks/use-mobile';
import { Button } from './Button';
import { Form } from './FormField';
import { useSaveNotice } from './save-notice';

/**
 * The outcome of saving a section: saved (with its own notice, if any), kept open with an already
 * translated reason, or kept open as it is (field errors already set, or the person cancelled a
 * confirmation).
 */
export type EditResult =
  | { status: 'saved'; notice?: string }
  | { status: 'conflict' | 'rejected'; reason: string }
  | { status: 'kept' };

export interface EditSheetProps<TValues extends FieldValues, TOutput extends FieldValues = TValues> {
  /** Already translated, e.g. "Edit personal data". */
  title: string;
  description?: string;
  /** Already translated, lower case, e.g. "personal data": completes the "Edit" button's name. */
  sectionName: string;
  /** The section's form (`useAppForm` with the section's slice of the schema). */
  form: UseFormReturn<TValues, unknown, TOutput>;
  /** The section's current values; the fields start from them each time the panel opens. */
  values: TValues;
  /**
   * Merges the section into the record and saves it. On a conflict, reset the form to the server's
   * values before returning, so the person can review them.
   */
  onSave: (values: TOutput) => Promise<EditResult>;
  /** The section's `FormField`s. */
  children: ReactNode;
  /** Already translated; "Changes saved" by default. */
  savedText?: string;
  /**
   * The trigger's own visible label and icon, e.g. "Add milestone" for a panel that creates a row;
   * "Edit" and `sectionName` otherwise. `context` completes its name for screen readers when the
   * page has several such buttons, e.g. "Plan" + "the powder day".
   */
  trigger?: { label: string; icon?: LucideIcon; context?: string };
  /** The trigger button, e.g. to move focus to it after a row it added is removed. */
  triggerRef?: Ref<HTMLButtonElement>;
  /**
   * Hides the trigger without closing an open panel, e.g. when the section stops being editable
   * while it is open: the panel then still shows why its save was refused.
   */
  hideTrigger?: boolean;
  /**
   * Controlled mode, without a trigger: one panel for a long list whose rows open it, e.g. the
   * distribution capture screen. Focus returns to whatever opened it.
   */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  /** Already translated; "Save changes" by default, e.g. "Record handover" for a panel that creates. */
  submitLabel?: string;
  /** Other actions on the record, at the start of the footer, e.g. removing it. */
  footerStart?: ReactNode;
  /**
   * Controlled mode: where focus goes on close when what opened the panel is gone, e.g. a conflict's
   * button that the save resolved (WCAG 2.4.3).
   */
  fallbackFocus?: () => HTMLElement | null;
}

/**
 * Edits one section of a detail page without leaving it (spec: Detail pages in read mode): an
 * "Edit" button opens the fields in a side panel, a bottom sheet on phones. Saving validates like the
 * register form; on success the panel closes, "Changes saved" is announced and focus returns to
 * "Edit". Cancel, Escape or closing discard the unsaved values. A rejected save keeps the panel
 * open with the reason in the error summary.
 */
export function EditSheet<TValues extends FieldValues, TOutput extends FieldValues = TValues>({
  title,
  description,
  sectionName,
  form,
  values,
  onSave,
  children,
  savedText,
  trigger,
  triggerRef,
  hideTrigger = false,
  open: controlledOpen,
  onOpenChange: onControlledOpenChange,
  submitLabel,
  footerStart,
  fallbackFocus,
}: EditSheetProps<TValues, TOutput>) {
  const { t } = useTranslation('ui');
  const [uncontrolledOpen, setUncontrolledOpen] = useState(false);
  const controlled = controlledOpen !== undefined;
  const open = controlled ? controlledOpen : uncontrolledOpen;
  const setOpen = (next: boolean) => {
    if (controlled) onControlledOpenChange?.(next);
    else setUncontrolledOpen(next);
  };
  // A controlled panel starts from the record each time it opens, as the trigger does.
  const wasOpen = useRef(false);
  // Without a trigger Radix has nowhere to return focus: back to what opened the panel (WCAG 2.4.3).
  const opener = useRef<HTMLElement | null>(null);
  useEffect(() => {
    if (controlled && open && !wasOpen.current) {
      opener.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
      form.reset(values);
    }
    wasOpen.current = open;
  }, [controlled, open, form, values]);
  const notify = useSaveNotice();
  const side = useIsMobile() ? 'bottom' : 'right';
  const { isSubmitting } = useFormState({ control: form.control });
  const saved = useRef<string | undefined>(undefined);

  const onOpenChange = (next: boolean) => {
    if (!next && isSubmitting) return; // A save in progress must report its outcome here.
    if (next && !controlled) form.reset(values); // Start from the record; closing discards what was typed.
    setOpen(next);
  };

  const submit = async (submitted: TOutput) => {
    let result: EditResult;
    try {
      result = await onSave(submitted);
    } catch {
      result = { status: 'rejected', reason: t('form.unexpectedError') };
    }
    if (result.status === 'saved') {
      saved.current = result.notice ?? savedText ?? t('detail.saved');
      setOpen(false);
      return;
    }
    if (result.status === 'kept') return;
    form.setError('root.server', { type: 'server', message: result.reason });
  };

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      {!hideTrigger && !controlled && (
        <SheetTrigger asChild>
          <ButtonPrimitive ref={triggerRef} variant="outline" size="sm" className="gap-1.5">
            {trigger ? (
              <>
                {trigger.icon && <trigger.icon aria-hidden="true" />}
                {trigger.label}
                {trigger.context && (
                  <>
                    {' '}
                    <span className="sr-only">{trigger.context}</span>
                  </>
                )}
              </>
            ) : (
              <>
                <Pencil aria-hidden="true" />
                {t('detail.edit')} <span className="sr-only">{sectionName}</span>
              </>
            )}
          </ButtonPrimitive>
        </SheetTrigger>
      )}
      <SheetContent
        side={side}
        data-side={side}
        // Without a description, tell Radix there is none on purpose.
        {...(description ? {} : { 'aria-describedby': undefined })}
        className="w-full gap-0 sm:max-w-xl"
        onCloseAutoFocus={(event) => {
          const target = opener.current?.isConnected ? opener.current : fallbackFocus?.();
          if (controlled && target) {
            event.preventDefault();
            target.focus();
          }
          // Announced once the panel is gone: while it is open the page is hidden from assistive
          // technology, and a notice inside it would be lost.
          const notice = saved.current;
          saved.current = undefined;
          if (notice) notify(notice);
        }}
      >
        <SheetHeader className="border-b px-6 pt-6 pb-4">
          <SheetTitle className="font-display text-section">{title}</SheetTitle>
          {description && <SheetDescription className="text-help">{description}</SheetDescription>}
        </SheetHeader>
        <Form form={form} onSubmit={submit} className="min-h-0 flex-1 scroll-pb-40 overflow-y-auto px-6 pt-4">
          {children}
          <div className="sticky bottom-0 -mx-6 mt-auto flex flex-wrap justify-end gap-2 border-t bg-popover px-6 py-4">
            {footerStart && <div className="me-auto flex flex-wrap gap-2">{footerStart}</div>}
            <SheetClose asChild>
              <Button variant="secondary" disabled={isSubmitting}>
                {t('confirm.cancel')}
              </Button>
            </SheetClose>
            <Button type="submit" pending={isSubmitting}>
              {submitLabel ?? t('detail.saveChanges')}
            </Button>
          </div>
        </Form>
      </SheetContent>
    </Sheet>
  );
}
