import { Pencil } from 'lucide-react';
import { useRef, useState, type ReactNode } from 'react';
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
}: EditSheetProps<TValues, TOutput>) {
  const { t } = useTranslation('ui');
  const [open, setOpen] = useState(false);
  const notify = useSaveNotice();
  const side = useIsMobile() ? 'bottom' : 'right';
  const { isSubmitting } = useFormState({ control: form.control });
  const saved = useRef<string | undefined>(undefined);

  const onOpenChange = (next: boolean) => {
    if (!next && isSubmitting) return; // A save in progress must report its outcome here.
    if (next) form.reset(values); // Start from the record; closing discards what was typed.
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
      <SheetTrigger asChild>
        <ButtonPrimitive variant="outline" size="sm" className="gap-1.5">
          <Pencil aria-hidden="true" />
          {t('detail.edit')} <span className="sr-only">{sectionName}</span>
        </ButtonPrimitive>
      </SheetTrigger>
      <SheetContent
        side={side}
        data-side={side}
        // Without a description, tell Radix there is none on purpose.
        {...(description ? {} : { 'aria-describedby': undefined })}
        className="w-full gap-0 sm:max-w-xl"
        onCloseAutoFocus={() => {
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
            <SheetClose asChild>
              <Button variant="secondary" disabled={isSubmitting}>
                {t('confirm.cancel')}
              </Button>
            </SheetClose>
            <Button type="submit" pending={isSubmitting}>
              {t('detail.saveChanges')}
            </Button>
          </div>
        </Form>
      </SheetContent>
    </Sheet>
  );
}
