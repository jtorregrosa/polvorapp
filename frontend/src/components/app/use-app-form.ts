import { useForm, type FieldValues, type UseFormProps, type UseFormReturn } from 'react-hook-form';

/**
 * `useForm` with the validation rules of the design system (design D8): validate on submit, then
 * again on every change of a field that has an error, and never focus the first invalid field,
 * because {@link Form} moves focus to the error summary instead.
 */
export function useAppForm<
  TValues extends FieldValues,
  TContext = unknown,
  TOutput extends FieldValues = TValues,
>(props: UseFormProps<TValues, TContext, TOutput> = {}): UseFormReturn<TValues, TContext, TOutput> {
  return useForm<TValues, TContext, TOutput>({
    mode: 'onSubmit',
    reValidateMode: 'onChange',
    shouldFocusError: false,
    ...props,
  });
}
