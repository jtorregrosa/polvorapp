import { useRef, type ComponentProps, type Ref } from 'react';
import { useTranslation } from 'react-i18next';
import { Input } from '@/components/ui/input';
import { cn } from '@/lib/cn';
import { CLEARABLE_FIELD_CLASS, ClearFieldButton } from './clear-field';

/**
 * The value a {@link DateInput} reports while a date is only partly typed: the browser then exposes
 * an empty value, so without this a half-filled field would look empty ("required") and an optional
 * one would silently drop what was typed. Form schemas reject it as an incomplete date.
 */
export const INCOMPLETE_DATE = 'incomplete';

export type DateInputProps = Omit<
  ComponentProps<typeof Input>,
  'type' | 'value' | 'onChange' | 'min' | 'max'
> & {
  /** `yyyy-MM-dd`, `''` when empty, or {@link INCOMPLETE_DATE}. Form defaults must be `''`, never `null`. */
  value?: string;
  /** Receives the new value: `yyyy-MM-dd`, `''`, or {@link INCOMPLETE_DATE}. */
  onChange?: (value: string) => void;
  /** Earliest selectable date, `yyyy-MM-dd`. */
  min?: string;
  /** Latest selectable date, `yyyy-MM-dd`, e.g. `todayIso()` for "not in the future". */
  max?: string;
  /**
   * Shows a cross inside the field while a date is set, to clear it, for optional dates: some mobile pickers
   * cannot empty a date once chosen.
   */
  clearable?: boolean;
  /** Names the clear button after the field when a page has several, e.g. "From". */
  clearSubject?: string;
  ref?: Ref<HTMLInputElement>;
};

/**
 * A calendar date field. The native picker is accessible and mobile-friendly (NFR-01); it shows the
 * date in the browser's locale, while the value stays ISO `yyyy-MM-dd`. Use inside
 * {@link FormField}, which gives it its label, help text and error. `min` and `max` only guide the
 * picker and are not announced by screen readers: the form schema checks the bounds and its error
 * says them. Autofill is off by default, since these forms record other people's dates.
 */
export function DateInput({
  value,
  onChange,
  onBlur,
  onKeyDown,
  clearable = false,
  clearSubject,
  ref,
  disabled,
  ...props
}: DateInputProps) {
  const { t } = useTranslation('ui');
  const input = useRef<HTMLInputElement | null>(null);
  const shown = value === INCOMPLETE_DATE ? '' : value;

  const setRefs = (node: HTMLInputElement | null) => {
    input.current = node;
    if (typeof ref === 'function') {
      ref(node);
    } else if (ref) {
      ref.current = node;
    }
  };

  return (
    <div className="relative">
      <Input
        autoComplete="off"
        {...props}
        className={cn(clearable && CLEARABLE_FIELD_CLASS, props.className)}
        ref={setRefs}
        type="date"
        disabled={disabled}
        value={shown}
        onChange={(event) =>
          onChange?.(event.currentTarget.validity.badInput ? INCOMPLETE_DATE : event.currentTarget.value)
        }
        onBlur={(event) => {
          // Chromium fires no input event while a date is only partly typed: report it on leaving.
          if (event.currentTarget.validity.badInput && value !== INCOMPLETE_DATE) {
            onChange?.(INCOMPLETE_DATE);
          }
          onBlur?.(event);
        }}
        onKeyDown={(event) => {
          // Enter submits without leaving the field: report a partly typed date first.
          if (event.key === 'Enter' && event.currentTarget.validity.badInput && value !== INCOMPLETE_DATE) {
            onChange?.(INCOMPLETE_DATE);
          }
          onKeyDown?.(event);
        }}
      />
      {clearable && Boolean(value) && !disabled && (
        <ClearFieldButton
          label={clearSubject ? t('form.clearDateFor', { subject: clearSubject }) : t('form.clearDate')}
          onClear={() => {
            // A partly typed date shows segments React does not know about: empty the field itself.
            if (input.current) input.current.value = '';
            onChange?.('');
            input.current?.focus();
          }}
        />
      )}
    </div>
  );
}
