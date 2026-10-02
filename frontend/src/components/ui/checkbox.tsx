import * as React from 'react';
import { cn } from '@/lib/cn';
import { CheckIcon } from 'lucide-react';
import { Checkbox as CheckboxPrimitive } from 'radix-ui';

// Local edit (design D3): full-opacity focus outline, 20 px square box that never deforms,
// with a 28 px hit area (44 px on touch screens) for SC 2.5.8.
function Checkbox({ className, ...props }: React.ComponentProps<typeof CheckboxPrimitive.Root>) {
  return (
    <CheckboxPrimitive.Root
      data-slot="checkbox"
      className={cn(
        'peer relative aspect-square size-5 min-w-5 shrink-0 rounded-sm border border-input bg-card transition-[background-color,border-color] duration-100 after:absolute after:-inset-1 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-destructive data-[state=checked]:border-primary data-[state=checked]:bg-primary data-[state=checked]:text-primary-foreground pointer-coarse:after:-inset-3',
        className,
      )}
      {...props}
    >
      <CheckboxPrimitive.Indicator
        data-slot="checkbox-indicator"
        className="grid place-content-center text-current transition-none"
      >
        <CheckIcon className="size-3.5" />
      </CheckboxPrimitive.Indicator>
    </CheckboxPrimitive.Root>
  );
}

export { Checkbox };
