import { useId } from 'react';
import { TextInput } from './TextInput';

export interface SearchFieldProps {
  /** Already translated; names the search box. */
  label: string;
  /** Already translated; what the search looks at, shown as the box's placeholder (the label stays visible). */
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
        // Inside the box, so the search is as tall as the filters beside it.
        placeholder={hint}
        onChange={(event) => {
          onChange(event.target.value);
        }}
      />
    </div>
  );
}
