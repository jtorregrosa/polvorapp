import { cn } from 'cn';
import type { ParseKeys } from 'i18next';
import { Slot } from 'radix-ui';
import type { ReactNode } from 'react';
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

/**
 * `TOutput` is what the resolver hands to `onSubmit` when it transforms the values (e.g. a select
 * that holds `''` until chosen and submits a code); it defaults to the form's own values.
 */
export interface FormProps<TValues extends FieldValues, TOutput extends FieldValues = TValues> {
  form: UseFormReturn<TValues, unknown, TOutput>;
  onSubmit: SubmitHandler<TOutput>;
  children: ReactNode;
  /** Shows "Fields marked with * are required." above the fields (WCAG 3.3.2). */
  requiredNote?: boolean;
  className?: string;
}

/**
 * A form wired to React Hook Form (ADR-0011). Browser validation bubbles are disabled; errors are
 * shown by each FormField and the first invalid field receives focus on submit.
 */
export function Form<TValues extends FieldValues, TOutput extends FieldValues = TValues>({
  form,
  onSubmit,
  children,
  requiredNote = true,
  className,
}: FormProps<TValues, TOutput>) {
  const { t } = useTranslation('ui');

  return (
    <FormProvider {...form}>
      <form
        noValidate
        className={cn('flex flex-col gap-6', className)}
        onSubmit={(e) => {
          if (form.formState.isSubmitting) {
            e.preventDefault(); // One submission at a time (Enter while the button is pending).
            return;
          }
          void form.handleSubmit(onSubmit)(e);
        }}
      >
        {requiredNote && <p className="text-sm text-muted-foreground">{t('form.requiredNote')}</p>}
        {children}
      </form>
    </FormProvider>
  );
}

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
  label: string;
  description?: string;
  required?: boolean;
  /** Renders the control; spread the props onto it so value, focus and a11y wiring work. */
  children: (field: FieldControlProps<TValues, TName>) => ReactNode;
}

/**
 * Wires the control to its label, help text and error: only ids of rendered elements are
 * referenced, and `aria-required` reaches any control (native or Radix).
 */
function FieldControl({
  hasDescription,
  required,
  children,
}: {
  hasDescription: boolean;
  required: boolean;
  children: ReactNode;
}) {
  const { error, formItemId, formDescriptionId, formMessageId } = useFormField();
  const describedBy = [hasDescription && formDescriptionId, error?.message && formMessageId]
    .filter(Boolean)
    .join(' ');

  return (
    <Slot.Root
      data-slot="form-control"
      id={formItemId}
      aria-describedby={describedBy || undefined}
      aria-invalid={Boolean(error)}
      aria-required={required || undefined}
    >
      {children}
    </Slot.Root>
  );
}

/** Translated validation message: Zod messages are keys of the `ui` namespace. */
function FieldError() {
  const { t } = useTranslation('ui');
  const { error, formMessageId } = useFormField();
  const message = error?.message;
  if (!message) {
    return null;
  }
  return (
    <p
      id={formMessageId}
      role="alert"
      data-slot="form-message"
      className="text-sm font-medium text-destructive"
    >
      {t(message as ParseKeys<'ui'>, { defaultValue: message })}
    </p>
  );
}

/**
 * Label, required marker, help text and translated error around one control (spec: Form fields
 * and validation messages).
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
  required = false,
  children,
}: FormFieldProps<TValues, TName, TOutput>) {
  return (
    <FormFieldController
      // Sound: a field registers and reads its own value; what the resolver submits does not
      // matter to it (the vendored controller has no type parameter for it).
      control={control as unknown as Control<TValues>}
      name={name}
      render={({ field }) => (
        <FormItem>
          <FormLabel>
            {label}
            {required && (
              <span aria-hidden="true" className="text-destructive">
                *
              </span>
            )}
          </FormLabel>
          <FieldControl hasDescription={Boolean(description)} required={required}>
            {children({ ...field, required })}
          </FieldControl>
          {description && <FormDescription>{description}</FormDescription>}
          <FieldError />
        </FormItem>
      )}
    />
  );
}
