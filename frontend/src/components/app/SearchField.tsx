import { useId } from 'react';
import { TextInput } from './TextInput';

export interface SearchFieldProps {
  /** Already translated; names the search box. */
  label: string;
  /** Already translated; what the search looks at, linked as the box's description. */
  hint?: string;
  value: string;
  onChange: (value: string) => void;
}

/**
 * A labelled search box above a list, next to its `FilterSelect`s (spec: Data tables). It filters
 * rows already loaded; the term is never put in the address, because it may be personal data
 * such as a DNI.
 */
export function SearchField({ label, hint, value, onChange }: SearchFieldProps) {
  const id = useId();
  const hintId = useId();
  return (
    <div className="grid gap-1.5">
      <label htmlFor={id} className="text-sm font-medium">
        {label}
      </label>
      <TextInput
        id={id}
        type="search"
        autoComplete="off"
        value={value}
        aria-describedby={hint ? hintId : undefined}
        onChange={(event) => {
          onChange(event.target.value);
        }}
      />
      {hint && (
        <p id={hintId} className="text-sm text-muted-foreground">
          {hint}
        </p>
      )}
    </div>
  );
}
