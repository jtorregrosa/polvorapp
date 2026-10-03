import { X } from 'lucide-react';
import { useRef, type ComponentProps, type Ref } from 'react';
import { useTranslation } from 'react-i18next';
import { Input } from '@/components/ui/input';
import { Button } from './Button';

/**
 * The value a {@link TimeInput} reports while a time is only partly typed: the browser then exposes
 * an empty value, so without this a half-filled field would look empty. Form schemas reject it as an
 * incomplete time.
 */
export const INCOMPLETE_TIME = 'incomplete';

/** Seconds per step of the native picker: whole minutes, so the value stays `HH:mm`. */
const MINUTE_STEP = 60;

export type TimeInputProps = Omit<ComponentProps<typeof Input>, 'type' | 'value' | 'onChange' | 'step'> & {
  /** `HH:mm`, `''` when empty, or {@link INCOMPLETE_TIME}. Form defaults must be `''`, never `null`. */
  value?: string;
  /** Receives the new value: `HH:mm`, `''`, or {@link INCOMPLETE_TIME}. */
  onChange?: (value: string) => void;
  /**
   * Shows a "clear time" button while a time is set, for optional times: some mobile pickers
   * cannot empty a time once chosen.
   */
  clearable?: boolean;
  /**
   * Already translated: whose time it is, when several times share a form, e.g. the comparsa. The
   * clear button is then named "Clear time of …", which starts with its visible text (WCAG 2.5.3).
   */
  clearSubject?: string;
  ref?: Ref<HTMLInputElement>;
};

/** Hours and minutes of a native time value, which may carry seconds. */
const toMinutes = (value: string) => value.slice(0, 5);

/**
 * A time-of-day field. The native picker is accessible and mobile-friendly (NFR-01); it shows the
 * time in the browser's locale (24 h or 12 h), while the value stays `HH:mm`. Use inside
 * {@link FormField}, which gives it its label, help text and error.
 */
export function TimeInput({
  value,
  onChange,
  onBlur,
  onKeyDown,
  clearable = false,
  clearSubject,
  ref,
  disabled,
  ...props
}: TimeInputProps) {
  const { t } = useTranslation('ui');
  const input = useRef<HTMLInputElement | null>(null);
  const shown = value === INCOMPLETE_TIME ? '' : value;

  const setRefs = (node: HTMLInputElement | null) => {
    input.current = node;
    if (typeof ref === 'function') {
      ref(node);
    } else if (ref) {
      ref.current = node;
    }
  };

  return (
    <div className="flex items-center gap-2">
      <Input
        autoComplete="off"
        {...props}
        ref={setRefs}
        type="time"
        step={MINUTE_STEP}
        disabled={disabled}
        value={shown}
        onChange={(event) =>
          onChange?.(
            event.currentTarget.validity.badInput ? INCOMPLETE_TIME : toMinutes(event.currentTarget.value),
          )
        }
        onBlur={(event) => {
          // Chromium fires no input event while a time is only partly typed: report it on leaving.
          if (event.currentTarget.validity.badInput && value !== INCOMPLETE_TIME) {
            onChange?.(INCOMPLETE_TIME);
          }
          onBlur?.(event);
        }}
        onKeyDown={(event) => {
          // Enter submits without leaving the field: report a partly typed time first.
          if (event.key === 'Enter' && event.currentTarget.validity.badInput && value !== INCOMPLETE_TIME) {
            onChange?.(INCOMPLETE_TIME);
          }
          onKeyDown?.(event);
        }}
      />
      {clearable && Boolean(value) && !disabled && (
        <Button
          type="button"
          variant="quiet"
          size="sm"
          icon={X}
          aria-label={clearSubject ? t('form.clearTimeFor', { subject: clearSubject }) : undefined}
          onClick={() => {
            // A partly typed time shows segments React does not know about: empty the field itself.
            if (input.current) input.current.value = '';
            onChange?.('');
            input.current?.focus();
          }}
        >
          {t('form.clearTime')}
        </Button>
      )}
    </div>
  );
}
