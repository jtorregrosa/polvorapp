import { useId } from 'react';
import { isIsoDate } from '@/lib/dates';
import { DateInput } from './DateInput';

export interface FilterDateProps {
  /** Already translated, e.g. "From". */
  label: string;
  /** `yyyy-MM-dd`, or `''` for no date. */
  value: string;
  /** Receives a complete `yyyy-MM-dd` date, or `''` once the field is emptied; a half-typed date is not reported. */
  onChange: (value: string) => void;
  min?: string;
  max?: string;
  /** Already translated, e.g. why the period is not valid; shown above the field. */
  error?: string;
}

/** A labelled date above a list, e.g. "From" and "To" of a period (add-audit-privacy, design D12). */
export function FilterDate({ label, value, onChange, min, max, error }: FilterDateProps) {
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
      <DateInput
        id={id}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        clearSubject={label}
        value={value}
        min={min}
        max={max}
        clearable
        onChange={(next) => {
          if (next === '' || isIsoDate(next)) onChange(next);
        }}
      />
    </div>
  );
}
