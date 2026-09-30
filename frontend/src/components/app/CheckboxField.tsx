import { useId } from 'react';
import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';

export interface CheckboxFieldProps {
  /** Already translated. */
  label: string;
  /** Help text under the label, linked with `aria-describedby`. */
  description?: string;
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
  disabled?: boolean;
}

/** A yes/no option with its label beside the box (the whole label toggles it). */
export function CheckboxField({
  label,
  description,
  checked,
  onCheckedChange,
  disabled,
}: CheckboxFieldProps) {
  const id = useId();
  const descriptionId = `${id}-description`;

  return (
    <div className="flex items-start gap-3">
      <Checkbox
        id={id}
        checked={checked}
        disabled={disabled}
        aria-describedby={description ? descriptionId : undefined}
        onCheckedChange={(value) => {
          onCheckedChange(value === true);
        }}
        className="mt-0.5"
      />
      <div className="grid gap-1">
        <Label htmlFor={id} className="leading-snug font-normal">
          {label}
        </Label>
        {description && (
          <p id={descriptionId} className="text-sm text-muted-foreground">
            {description}
          </p>
        )}
      </div>
    </div>
  );
}
