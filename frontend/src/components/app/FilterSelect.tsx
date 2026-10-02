import { useId, type Ref } from 'react';
import { SelectInput, type SelectOption } from './SelectInput';

export interface FilterSelectProps {
  /** Already translated; names the select. */
  label: string;
  /** The chosen value; `''` for "all". */
  value: string;
  options: readonly SelectOption[];
  onChange: (value: string) => void;
  /** Shown while nothing is chosen, never offered as a choice (see `SelectInput`). */
  placeholder?: string;
  /** Already translated; says what is wrong with the choice, between the label and the select. */
  error?: string;
  ref?: Ref<HTMLSelectElement>;
}

/** A labelled select above a list, e.g. "Side: All / Moorish / Christian" (spec: Data tables). */
export function FilterSelect({
  label,
  value,
  options,
  onChange,
  placeholder,
  error,
  ref,
}: FilterSelectProps) {
  const id = useId();
  const errorId = useId();
  return (
    <div className="grid gap-field">
      <label htmlFor={id} className="text-label">
        {label}
      </label>
      {error && (
        <p id={errorId} className="text-label text-destructive">
          {error}
        </p>
      )}
      <SelectInput
        ref={ref}
        id={id}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        value={value}
        options={options}
        placeholder={placeholder}
        onChange={(event) => {
          onChange(event.target.value);
        }}
      />
    </div>
  );
}
