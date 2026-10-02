import { useId, useImperativeHandle, useRef, type ReactNode, type Ref } from 'react';
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group';

export interface RadioCardOption {
  value: string;
  /** Already translated. */
  label: string;
  /** Already translated; read as the option's description. */
  hint?: string;
  /** Fields that apply only to this answer, shown under the group while it is chosen. */
  reveal?: ReactNode;
}

export interface RadioCardsProps {
  options: readonly RadioCardOption[];
  value?: string | null;
  onChange: (value: string) => void;
  onBlur?: () => void;
  name?: string;
  disabled?: boolean;
  required?: boolean;
  /** Receives `focus()`, which focuses the chosen or the first option (React Hook Form). */
  ref?: Ref<{ focus: () => void }>;
  id?: string;
  'aria-labelledby'?: string;
  'aria-describedby'?: string;
  'aria-invalid'?: boolean;
  'aria-required'?: boolean;
}

/**
 * A choice among two to four options shown as cards (spec: Form fields; design D8). The card is
 * the label, the dot sits in a fixed square box so it never turns oval, and the fields of an
 * answer that need them appear under the group while that answer is chosen. Use inside
 * {@link FormField}, which names and describes the group.
 */
export function RadioCards({
  options,
  value,
  onChange,
  onBlur,
  name,
  disabled,
  required,
  ref,
  id,
  'aria-labelledby': labelledBy,
  'aria-describedby': describedBy,
  'aria-invalid': invalid,
  'aria-required': ariaRequired,
}: RadioCardsProps) {
  const baseId = useId();
  const group = useRef<HTMLDivElement>(null);
  const chosen = options.find((option) => option.value === value);

  useImperativeHandle(
    ref,
    () => ({
      focus: () => {
        const radios = group.current?.querySelectorAll<HTMLElement>('[role="radio"]');
        const checked = group.current?.querySelector<HTMLElement>('[role="radio"][aria-checked="true"]');
        (checked ?? radios?.[0])?.focus();
      },
    }),
    [],
  );

  return (
    <div className="flex flex-col gap-group">
      <RadioGroup
        ref={group}
        id={id}
        name={name}
        value={value ?? ''}
        onValueChange={onChange}
        onBlur={onBlur}
        disabled={disabled}
        required={required}
        aria-labelledby={labelledBy}
        aria-describedby={describedBy}
        aria-invalid={invalid}
        aria-required={ariaRequired}
        className="flex flex-wrap gap-2"
      >
        {options.map((option) => {
          const itemId = `${baseId}-${option.value}`;
          return (
            <label
              key={option.value}
              htmlFor={itemId}
              className="flex min-h-control min-w-36 flex-1 cursor-pointer items-start gap-3 rounded-lg border border-input bg-card px-3 py-2.5 hover:bg-surface-2 has-disabled:cursor-not-allowed has-disabled:opacity-50 has-data-[state=checked]:border-primary has-data-[state=checked]:bg-primary-soft"
            >
              <RadioGroupItem
                id={itemId}
                value={option.value}
                aria-labelledby={`${itemId}-label`}
                aria-describedby={option.hint ? `${itemId}-hint` : undefined}
                className="mt-px"
              />
              <span className="flex min-w-0 flex-col gap-0.5">
                <span id={`${itemId}-label`} className="text-label break-words text-foreground">
                  {option.label}
                </span>
                {option.hint && (
                  <span id={`${itemId}-hint`} className="text-help break-words text-muted-foreground">
                    {option.hint}
                  </span>
                )}
              </span>
            </label>
          );
        })}
      </RadioGroup>
      {chosen?.reveal && (
        <div data-slot="radio-cards-reveal" className="flex flex-col gap-group border-l-4 border-border pl-4">
          {chosen.reveal}
        </div>
      )}
    </div>
  );
}
