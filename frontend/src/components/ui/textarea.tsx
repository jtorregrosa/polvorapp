import * as React from 'react';
import { cn } from '@/lib/cn';

// Local edit (design D3): full-opacity focus outline, card surface in both themes.
function Textarea({ className, ...props }: React.ComponentProps<'textarea'>) {
  return (
    <textarea
      data-slot="textarea"
      className={cn(
        'flex field-sizing-content min-h-16 w-full rounded-md border border-input bg-card px-3 py-2 text-base transition-[color,border-color] duration-100 placeholder:text-muted-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-destructive md:text-body',
        className,
      )}
      {...props}
    />
  );
}

export { Textarea };
