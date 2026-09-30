import type { ComponentProps } from 'react';
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select';

export interface SelectOption {
  value: string;
  /** Already translated. */
  label: string;
  /** Language of the label when it differs from the page (e.g. language names). */
  lang?: string;
}

export type SelectInputProps = Omit<ComponentProps<typeof NativeSelect>, 'children'> & {
  options: readonly SelectOption[];
};

/**
 * A choice among a few options. A native select keeps the platform's accessible picker on
 * phones. Use inside {@link FormField}, or with its own label for filters.
 */
export function SelectInput({ options, ...props }: SelectInputProps) {
  return (
    <NativeSelect {...props}>
      {options.map((option) => (
        <NativeSelectOption key={option.value} value={option.value} lang={option.lang}>
          {option.label}
        </NativeSelectOption>
      ))}
    </NativeSelect>
  );
}
