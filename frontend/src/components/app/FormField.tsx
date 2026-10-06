import { cn } from '@/lib/cn';
import type { ParseKeys } from 'i18next';
import { Slot } from 'radix-ui';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import {
  type Control,
  type ControllerRenderProps,
  type FieldPath,
  type FieldValues,
  type SubmitHandler,
  type UseFormReturn,
} from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import {
  Form as FormProvider,
  FormDescription,
  FormField as FormFieldController,
  FormItem,
  FormLabel,
  useFormField,
} from '@/components/ui/form';
import { ErrorSummary, type ErrorSummaryItem } from './ErrorSummary';

/**
 * `TOutput` is what the resolver hands to `onSubmit` when it transforms the values (e.g. a select
 * that holds `''` until chosen and submits a code); it defaults to the form's own values.
 */
export interface FormProps<TValues extends FieldValues, TOutput extends FieldValues = TValues> {
  /** Create it with `useAppForm`, which validates on submit and leaves focus to the summary. */
  form: UseFormReturn<TValues, unknown, TOutput>;
  /** May set server errors with `form.setError`; they are listed in the summary too. */
  onSubmit: SubmitHandler<TOutput>;
  children: ReactNode;
  /**
   * States once, at the start, that fields are required unless marked "(optional)". Turn it off
   * only when the form has a single field or the page already says it.
   */
  requiredNote?: boolean;
  className?: string;
}

/** Translates a validation message: Zod and server messages are keys of a namespace. */
function useMessage() {
  const { t } = useTranslation('ui');
  return (message: string) => t(message as ParseKeys<'ui'>, { defaultValue: message });
}

/** Every error message of an RHF error tree, with its dotted path (nested and `root.*` errors). */
function flattenErrors(tree: object, prefix = ''): { path: string; message: string }[] {
  return Object.entries(tree).flatMap(([key, value]: [string, unknown]) => {
    if (key === 'ref' || typeof value !== 'object' || value === null) return [];
    const path = prefix ? `${prefix}.${key}` : key;
    const own = (value as { message?: unknown }).message;
    const nested = flattenErrors(value, path);
    return typeof own === 'string' && own !== '' ? [{ path, message: own }, ...nested] : nested;
  });
}

/**
 * The summary entries of the current errors (design D8): first the errors of the fields on screen,
 * in screen order (found through their `data-field-name`, so conditional fields keep their place),
 * then any other error (a form-level `root` error, or a field that is not shown) without a link,
 * so no error is ever dropped. Read while rendering: errors only exist once the fields are mounted.
 */
function useSummaryItems<TValues extends FieldValues, TOutput extends FieldValues>(
  form: UseFormReturn<TValues, unknown, TOutput>,
  formElement: HTMLFormElement | null,
): ErrorSummaryItem[] {
  const message = useMessage();
  const { errors } = form.formState;
  const all = flattenErrors(errors);
  if (all.length === 0 && Object.keys(errors).length > 0)
    all.push({ path: 'root', message: 'form.unexpectedError' });
  if (all.length === 0) return [];
  const fields = formElement ? [...formElement.querySelectorAll<HTMLElement>('[data-field-name]')] : [];
  const linked = new Set<string>();
  const fieldItems = fields.flatMap((field) => {
    const name = field.dataset.fieldName ?? '';
    const error = all.find((entry) => entry.path === name);
    if (!error || linked.has(name)) return [];
    linked.add(name);
    return [
      {
        text: `${field.dataset.fieldLabel ?? ''}: ${message(error.message)}`,
        fieldId: field.dataset.fieldControl,
      },
    ];
  });
  const unlinked = all.filter((entry) => !linked.has(entry.path));
  const isRoot = (path: string) => path === 'root' || path.startsWith('root.');
  const toItem = (entry: { message: string }): ErrorSummaryItem => ({ text: message(entry.message) });
  return [
    ...unlinked.filter((entry) => isRoot(entry.path)).map(toItem),
    ...fieldItems,
    ...unlinked.filter((entry) => !isRoot(entry.path)).map(toItem),
  ];
}

/**
 * A form wired to React Hook Form (ADR-0011, design D8). Browser validation bubbles are off. A
 * failed submission, or server errors set while submitting, show an {@link ErrorSummary} at the
 * top that receives focus once per submission; each error is also shown at its field. One
 * submission at a time. `onSubmit` should report its own failures; an unexpected rejection is shown
 * as a generic error in the summary.
 */
export function Form<TValues extends FieldValues, TOutput extends FieldValues = TValues>({
  form,
  onSubmit,
  children,
  requiredNote = true,
  className,
}: FormProps<TValues, TOutput>) {
  const { t } = useTranslation('ui');
  const [formElement, setFormElement] = useState<HTMLFormElement | null>(null);
  const summary = useRef<HTMLDivElement>(null);
  const submitting = useRef(false);
  const items = useSummaryItems(form, formElement);
  const { submitCount } = form.formState;
  const showSummary = submitCount > 0 && items.length > 0;

  // RHF publishes the errors and the new submit count together, after validation or after
  // `onSubmit` (server errors included). Focus moves once per submission, never while the
  // person fixes a field.
  useEffect(() => {
    if (submitCount > 0) summary.current?.focus();
  }, [submitCount]);

  return (
    <FormProvider {...form}>
      <form
        ref={setFormElement}
        noValidate
        className={cn('flex flex-col gap-group', className)}
        onSubmit={(event) => {
          if (submitting.current) {
            event.preventDefault(); // One submission at a time (Enter while the button is pending).
            return;
          }
          submitting.current = true;
          void form
            .handleSubmit(onSubmit)(event)
            .catch(() => {
              // `onSubmit` should report its own failures; an unexpected one still reaches the summary.
              form.setError('root.unexpected', { type: 'unexpected', message: 'form.unexpectedError' });
            })
            .finally(() => {
              submitting.current = false;
            });
        }}
      >
        {showSummary && <ErrorSummary ref={summary} title={t('form.errorSummaryTitle')} items={items} />}
        {requiredNote && <p className="text-help text-muted-foreground">{t('form.requiredNote')}</p>}
        {children}
      </form>
    </FormProvider>
  );
}

/** How wide the control is, after the content it expects (spec: Form fields). */
export type FieldWidth = 'id' | 'short' | 'name' | 'long' | 'full';

const WIDTHS: Record<FieldWidth, string> = {
  id: 'max-w-field-id', // nationalId, federationId, codes
  short: 'max-w-field-short', // dates, phone numbers, numbers
  name: 'max-w-field-name', // names, selects of names
  long: 'max-w-field-long', // email addresses, long names
  full: '', // free text
};

export type FieldControlProps<
  TValues extends FieldValues,
  TName extends FieldPath<TValues>,
> = ControllerRenderProps<TValues, TName> & { required?: boolean };

export interface FormFieldProps<
  TValues extends FieldValues,
  TName extends FieldPath<TValues>,
  TOutput extends FieldValues = TValues,
> {
  /** The form's control; `TOutput` is what its resolver submits (see {@link FormProps}). */
  control: Control<TValues, unknown, TOutput>;
  name: TName;
  /** Already translated; "(optional)" is added for optional fields. */
  label: string;
  description?: string;
  /** Fields are required unless optional; the label then ends in "(optional)". */
  optional?: boolean;
  /** Limits the control's width to its expected content; free text takes the full width. */
  width?: FieldWidth;
  /** Renders the control; spread the props onto it so value, focus and a11y wiring work. */
  children: (field: FieldControlProps<TValues, TName>) => ReactNode;
}

/**
 * Wires the control to its label, help text and error: only ids of rendered elements are
 * referenced, the label also names groups (radio cards), and `aria-required` reaches any control.
 */
function FieldControl({
  name,
  label,
  hasDescription,
  required,
  width,
  children,
}: {
  name: string;
  /** The full label, "(optional)" included, for the error summary. */
  label: string;
  hasDescription: boolean;
  required: boolean;
  width: FieldWidth;
  children: ReactNode;
}) {
  const { error, formItemId, formDescriptionId, formMessageId, id } = useFormField();
  const describedBy = [hasDescription && formDescriptionId, error?.message && formMessageId]
    .filter(Boolean)
    .join(' ');

  return (
    <div
      data-slot="form-field-control"
      data-field-name={name}
      data-field-label={label}
      data-field-control={formItemId}
      className={cn('w-full min-w-0', WIDTHS[width])}
    >
      <Slot.Root
        data-slot="form-control"
        id={formItemId}
        aria-labelledby={`${id}-form-item-label`}
        aria-describedby={describedBy || undefined}
        aria-invalid={Boolean(error)}
        aria-required={required || undefined}
      >
        {children}
      </Slot.Root>
    </div>
  );
}

/** The field's translated error, read with the field through `aria-describedby`. */
function FieldError() {
  const { t } = useTranslation('ui');
  const message = useMessage();
  const { error, formMessageId } = useFormField();
  if (!error?.message) {
    return null;
  }
  return (
    <p id={formMessageId} data-slot="form-message" className="text-help font-semibold text-destructive">
      <span className="sr-only">{`${t('alert.error')}:`}</span> {message(error.message)}
    </p>
  );
}

/** The label with "(optional)" for optional fields; it also names radio groups. */
function FieldLabel({ label, optional }: { label: string; optional: boolean }) {
  const { t } = useTranslation('ui');
  const { id } = useFormField();
  return (
    <FormLabel
      id={`${id}-form-item-label`}
      className="block text-label text-foreground data-[error=true]:text-foreground"
    >
      {label}
      {optional && ' '}
      {optional && <span className="font-normal text-muted-foreground">{t('form.optionalSuffix')}</span>}
    </FormLabel>
  );
}

/**
 * Label, help text, translated error and control, in that order (spec: Form fields and validation
 * messages). Required unless `optional`; no asterisk. An invalid field gets a bar beside it.
 */
export function FormField<
  TValues extends FieldValues,
  TName extends FieldPath<TValues>,
  TOutput extends FieldValues = TValues,
>({
  control,
  name,
  label,
  description,
  optional = false,
  width = 'full',
  children,
}: FormFieldProps<TValues, TName, TOutput>) {
  return (
    <FormFieldController
      // Sound: a field registers and reads its own value; what the resolver submits does not
      // matter to it (the vendored controller has no type parameter for it).
      control={control as unknown as Control<TValues>}
      name={name}
      render={({ field, fieldState }) => (
        // The error bar is reserved beside the field; the negative margin puts it in the gutter, so the
        // label lines up with the section title instead of 16 px in (UI audit).
        <FormItem
          data-invalid={Boolean(fieldState.error)}
          className="-ms-4 gap-field border-s-4 border-transparent ps-3 data-[invalid=true]:border-destructive"
        >
          <FieldLabel label={label} optional={optional} />
          {description && <FormDescription className="text-help">{description}</FormDescription>}
          <FieldError />
          <FieldControl
            name={name}
            label={label}
            hasDescription={Boolean(description)}
            required={!optional}
            width={width}
          >
            {children({ ...field, required: !optional })}
          </FieldControl>
        </FormItem>
      )}
    />
  );
}
