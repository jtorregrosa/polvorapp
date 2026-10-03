import { useEffect, useId, useState, type ChangeEvent, type ComponentProps } from 'react';
import { useTranslation } from 'react-i18next';
import { Textarea } from '@/components/ui/textarea';
import { cn } from '@/lib/cn';

/** How long typing must pause before the count is announced, so a screen reader is not flooded. */
export const COUNT_ANNOUNCE_DELAY_MS = 1000;

export type TextAreaProps = Omit<ComponentProps<typeof Textarea>, 'maxLength'> & {
  /** The most characters the text may have; the form's schema refuses more. */
  maxLength: number;
};

/**
 * A multi-line text field with a character count (add-comparsa-orders, design D12), as the GOV.UK
 * character count: the limit is part of its description; the characters left, or over the limit,
 * show under it and are announced politely once typing pauses. Nothing is cut silently: the field
 * takes any length and the form's schema refuses a text over the limit. Use inside
 * {@link FormField}, which gives it its label, help text and error.
 */
export function TextArea({
  maxLength,
  value,
  defaultValue,
  onChange,
  id,
  'aria-describedby': describedBy,
  ...props
}: TextAreaProps) {
  const { t } = useTranslation('ui');
  const ownId = useId();
  const fieldId = id ?? ownId;
  const limitId = `${fieldId}-limit`;
  const [typed, setTyped] = useState(() => String(defaultValue ?? '').length);
  const length = value === undefined ? typed : String(value).length;
  const over = length > maxLength;
  const count = over
    ? t('textArea.over', { count: length - maxLength })
    : t('textArea.remaining', { count: maxLength - length });
  const [announced, setAnnounced] = useState('');
  // Set by typing only, so a reset or a language switch announces nothing; each keystroke restarts the wait.
  const [pending, setPending] = useState(false);

  useEffect(() => {
    if (!pending) return undefined;
    const timer = window.setTimeout(() => {
      setAnnounced(count);
      setPending(false);
    }, COUNT_ANNOUNCE_DELAY_MS);
    return () => {
      window.clearTimeout(timer);
    };
  }, [pending, count]);

  return (
    <div className="flex flex-col gap-field">
      <Textarea
        {...props}
        id={fieldId}
        value={value}
        defaultValue={defaultValue}
        aria-describedby={[describedBy, limitId].filter(Boolean).join(' ')}
        onChange={(event: ChangeEvent<HTMLTextAreaElement>) => {
          setTyped(event.target.value.length);
          setPending(true);
          onChange?.(event);
        }}
      />
      <span id={limitId} className="sr-only">
        {t('textArea.limit', { max: maxLength })}
      </span>
      <p
        aria-hidden="true"
        data-over={over}
        className={cn('text-help text-muted-foreground', over && 'font-semibold text-destructive')}
      >
        {count}
      </p>
      <span role="status" className="sr-only">
        {announced}
      </span>
    </div>
  );
}
