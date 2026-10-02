import * as React from 'react';
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '@/lib/cn';
import { Slot } from 'radix-ui';

// Local edit (design D3): full-opacity focus outline, solid hover tokens, the destructive token pair,
// token heights (44 px on touch screens) and only colour, shadow and press motion.
const buttonVariants = cva(
  "inline-flex shrink-0 items-center justify-center gap-2 rounded-md text-label font-semibold whitespace-nowrap transition-[color,background-color,border-color,box-shadow,transform] duration-100 ease-out focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring active:scale-98 disabled:pointer-events-none disabled:opacity-50 aria-invalid:border-destructive [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
  {
    variants: {
      variant: {
        default: 'bg-primary text-primary-foreground shadow-e1 hover:bg-primary-hover',
        destructive: 'bg-destructive text-destructive-foreground shadow-e1 hover:bg-destructive-hover',
        outline: 'border bg-secondary text-secondary-foreground shadow-e1 hover:bg-secondary-hover',
        secondary: 'border bg-secondary text-secondary-foreground shadow-e1 hover:bg-secondary-hover',
        ghost: 'hover:bg-secondary-hover hover:text-foreground',
        link: 'text-primary underline-offset-4 hover:underline',
      },
      size: {
        default: 'h-control px-4 py-2 has-[>svg]:px-3',
        xs: "h-6 gap-1 rounded-md px-2 text-xs has-[>svg]:px-1.5 [&_svg:not([class*='size-'])]:size-3",
        sm: 'h-8 gap-1.5 rounded-md px-3 has-[>svg]:px-2.5 pointer-coarse:h-11',
        lg: 'h-11 rounded-md px-6 has-[>svg]:px-4',
        icon: 'size-control',
        'icon-xs': "size-6 rounded-md [&_svg:not([class*='size-'])]:size-3",
        'icon-sm': 'size-8 pointer-coarse:size-11',
        'icon-lg': 'size-10',
      },
    },
    defaultVariants: {
      variant: 'default',
      size: 'default',
    },
  },
);

function Button({
  className,
  variant = 'default',
  size = 'default',
  asChild = false,
  ...props
}: React.ComponentProps<'button'> &
  VariantProps<typeof buttonVariants> & {
    asChild?: boolean;
  }) {
  const Comp = asChild ? Slot.Root : 'button';

  return (
    <Comp
      data-slot="button"
      data-variant={variant}
      data-size={size}
      className={cn(buttonVariants({ variant, size, className }))}
      {...props}
    />
  );
}

export { Button, buttonVariants };
