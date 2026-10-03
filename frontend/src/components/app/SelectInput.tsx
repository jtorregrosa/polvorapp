import type { ComponentProps } from 'react';
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select';

export interface SelectOption {
  value: string;
  /** Already translated. */
  label: string;
  /** Language of the label when it differs from the page (e.g. language names). */
  lang?: string;
  /** Shown but not offered, e.g. a person who is not eligible; the label says why. */
  disabled?: boolean;
}

export type SelectInputProps = Omit<ComponentProps<typeof NativeSelect>, 'children' | 'placeholder'> & {
  options: readonly SelectOption[];
  /**
   * Already translated, e.g. "Choose a comparsa": shown while nothing is chosen, never offered as a
   * choice (spec: Styled select). An empty value that is a real choice ("All") is an option instead.
   */
  placeholder?: string;
};

/**
 * A choice in a longer or growing list (two to four options are radio cards, see `RadioCards`).
 * A native select keeps the platform's picker on phones; browsers that can style the list show it
 * in the menu style (design D11). Use inside {@link FormField}, or with its own label for filters.
 */
export function SelectInput({ options, placeholder, ...props }: SelectInputProps) {
  // A form field with nothing chosen yet (value undefined) shows the placeholder, not the first
  // option; an uncontrolled select (no onChange) is left as it is.
  const value = placeholder !== undefined && props.onChange && props.value == null ? '' : props.value;
  return (
    <NativeSelect {...props} value={value}>
      {placeholder !== undefined && (
        <NativeSelectOption value="" disabled hidden data-placeholder="">
          {placeholder}
        </NativeSelectOption>
      )}
      {options.map((option) => (
        <NativeSelectOption
          key={option.value}
          value={option.value}
          lang={option.lang}
          disabled={option.disabled}
        >
          {option.label}
        </NativeSelectOption>
      ))}
    </NativeSelect>
  );
}
