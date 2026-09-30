import type { ComponentProps } from 'react';
import { Input } from '@/components/ui/input';

export type TextInputProps = Omit<ComponentProps<typeof Input>, 'type'> & {
  type?: 'text' | 'email' | 'search' | 'tel' | 'url';
};

/**
 * A single-line text field. Use inside {@link FormField}, which gives it its label, help text and
 * error; set `autoComplete` so browsers and password managers can fill it.
 */
export function TextInput({ type = 'text', ...props }: TextInputProps) {
  return <Input type={type} {...props} />;
}
