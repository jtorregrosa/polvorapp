import * as React from 'react';
import { cn } from '@/lib/cn';
import { RadioGroup as RadioGroupPrimitive } from 'radix-ui';

function RadioGroup({ className, ...props }: React.ComponentProps<typeof RadioGroupPrimitive.Root>) {
  return (
    <RadioGroupPrimitive.Root data-slot="radio-group" className={cn('grid gap-3', className)} {...props} />
  );
}

// Local edit (design D3): full-opacity focus outline; a fixed 20 px square box (28 px hit area, 44 px on touch) so the dot never turns
// oval; the dot is an inset ring of the surface, and a thick border marks it in forced colours.
function RadioGroupItem({ className, ...props }: React.ComponentProps<typeof RadioGroupPrimitive.Item>) {
  return (
    <RadioGroupPrimitive.Item
      data-slot="radio-group-item"
      className={cn(
        'relative aspect-square size-5 min-w-5 shrink-0 rounded-full border border-input bg-card transition-[background-color,border-color] duration-100 after:absolute after:-inset-1 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-50 data-[state=checked]:border-primary data-[state=checked]:bg-primary data-[state=checked]:shadow-[inset_0_0_0_4px_var(--card)] forced-colors:data-[state=checked]:border-[6px] pointer-coarse:after:-inset-3',
        className,
      )}
      {...props}
    />
  );
}

export { RadioGroup, RadioGroupItem };
