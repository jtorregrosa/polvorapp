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
    <div className="grid gap-field">
      <label htmlFor={id} className="text-label">
        {label}
      </label>
      <TextInput
        id={id}
        type="search"
        autoComplete="off"
        // Searches by names and identity numbers: never sent to a cloud spellchecker.
        spellCheck={false}
        value={value}
        aria-describedby={hint ? hintId : undefined}
        onChange={(event) => {
          onChange(event.target.value);
        }}
      />
      {hint && (
        <p id={hintId} className="text-help text-muted-foreground">
          {hint}
        </p>
      )}
    </div>
  );
}
