import { useId } from 'react';
import { SelectInput, type SelectOption } from './SelectInput';

export interface FilterSelectProps {
  /** Already translated; names the select. */
  label: string;
  /** The chosen value; `''` for "all". */
  value: string;
  options: readonly SelectOption[];
  onChange: (value: string) => void;
}

/** A labelled select above a list, e.g. "Side: All / Moorish / Christian" (spec: Data tables). */
export function FilterSelect({ label, value, options, onChange }: FilterSelectProps) {
  const id = useId();
  return (
    <div className="grid gap-1.5">
      <label htmlFor={id} className="text-sm font-medium">
        {label}
      </label>
      <SelectInput
        id={id}
        value={value}
        options={options}
        onChange={(event) => {
          onChange(event.target.value);
        }}
      />
    </div>
  );
}
