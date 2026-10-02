import { FileUp, X } from 'lucide-react';
import { useId, useRef, useState, type Ref } from 'react';
import { useTranslation } from 'react-i18next';
import { useFormatters } from '@/lib/format';
import { Button } from './Button';
import { formatFileSize } from './file-rules';

export interface FileFieldProps {
  /** The chosen file, or null. */
  value: File | null;
  /** Receives the chosen file, or null when it is cleared. Validate it in the form's schema. */
  onChange: (file: File | null) => void;
  onBlur: () => void;
  /** Offered in the system's file chooser, e.g. `.xlsx`. */
  accept: string;
  disabled?: boolean;
  /** RHF's ref: the button, so a failed submission can focus the field. */
  ref?: Ref<HTMLButtonElement>;
  /** Set by {@link FormField} to wire the label, help text and error to the button. */
  id?: string;
  'aria-labelledby'?: string;
  'aria-describedby'?: string;
  'aria-invalid'?: boolean;
}

/**
 * Chooses one file to upload (spec: Import screen). A visible button opens the system's chooser;
 * the chosen file is shown with its size, is part of the button's description and is announced,
 * and can be cleared, after which focus returns to the button. Use inside {@link FormField}: its
 * label, help text and error are wired to the button, which also says what it does.
 */
export function FileField({
  value,
  onChange,
  onBlur,
  accept,
  disabled,
  ref,
  id,
  'aria-labelledby': ariaLabelledBy,
  'aria-describedby': ariaDescribedBy,
  'aria-invalid': ariaInvalid,
}: FileFieldProps) {
  const { t } = useTranslation('ui');
  const { number } = useFormatters();
  const input = useRef<HTMLInputElement>(null);
  const choose = useRef<HTMLButtonElement | null>(null);
  const [announcement, setAnnouncement] = useState('');
  const actionId = useId();
  const fileId = useId();
  const size = (file: File) => formatFileSize(file.size, number);

  // The forwarded ref (RHF) and the own one (focus after clearing) on the same button.
  const setChoose = (node: HTMLButtonElement | null) => {
    choose.current = node;
    if (typeof ref === 'function') ref(node);
    else if (ref) ref.current = node;
  };

  return (
    <div className="flex flex-wrap items-center gap-3">
      <input
        ref={input}
        type="file"
        accept={accept}
        disabled={disabled}
        hidden
        onChange={(event) => {
          const file = event.target.files?.[0] ?? null;
          // Choosing the same file again after clearing it must still be noticed.
          event.target.value = '';
          if (!file) return;
          onChange(file);
          setAnnouncement(t('fileField.chosenStatus', { name: file.name, size: size(file) }));
        }}
      />
      {/* Only the wiring a button may carry: it cannot be "required"; the form's schema enforces
          that. aria-invalid only styles it; the error itself is in its description. */}
      <Button
        ref={setChoose}
        id={id}
        aria-describedby={[fileId, ariaDescribedBy].filter(Boolean).join(' ')}
        aria-invalid={ariaInvalid}
        type="button"
        variant="secondary"
        icon={FileUp}
        disabled={disabled}
        aria-labelledby={[ariaLabelledBy, actionId].filter(Boolean).join(' ')}
        onBlur={onBlur}
        onClick={() => input.current?.click()}
      >
        <span id={actionId}>{value ? t('fileField.change') : t('fileField.choose')}</span>
      </Button>
      <span id={fileId} className="flex min-w-0 items-center gap-2 text-body">
        {value ? (
          <>
            <span className="font-medium break-all">{value.name}</span>{' '}
            {/* The space keeps name and size apart when read as the button's description. */}
            <span className="text-muted-foreground">{size(value)}</span>
          </>
        ) : (
          <span className="text-muted-foreground">{t('fileField.none')}</span>
        )}
      </span>
      {value && (
        <Button
          type="button"
          variant="quiet"
          size="sm"
          icon={X}
          disabled={disabled}
          aria-label={t('fileField.remove', { name: value.name })}
          onClick={() => {
            onChange(null);
            setAnnouncement(t('fileField.removedStatus', { name: value.name }));
            choose.current?.focus();
          }}
        />
      )}
      <span role="status" className="sr-only">
        {announcement}
      </span>
    </div>
  );
}
