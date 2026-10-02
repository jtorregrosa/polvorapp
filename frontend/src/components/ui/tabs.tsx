import * as React from 'react';
import { cn } from '@/lib/cn';
import { Tabs as TabsPrimitive } from 'radix-ui';

function Tabs({ className, ...props }: React.ComponentProps<typeof TabsPrimitive.Root>) {
  return (
    <TabsPrimitive.Root data-slot="tabs" className={cn('flex flex-col gap-section', className)} {...props} />
  );
}

// Local edit (design D3, D5): a line variant only, scrolling sideways instead of wrapping.
function TabsList({ className, ...props }: React.ComponentProps<typeof TabsPrimitive.List>) {
  return (
    <TabsPrimitive.List
      data-slot="tabs-list"
      className={cn('flex max-w-full gap-1 overflow-x-auto border-b', className)}
      {...props}
    />
  );
}

// Local edit (design D3, D5): a full-opacity focus outline, the ember bar under the chosen tab
// (also a weight change, never colour alone), and no transition: the bar moves at once.
function TabsTrigger({ className, ...props }: React.ComponentProps<typeof TabsPrimitive.Trigger>) {
  return (
    <TabsPrimitive.Trigger
      data-slot="tabs-trigger"
      className={cn(
        'relative inline-flex h-control shrink-0 items-center gap-1.5 rounded-t-md px-3 text-label whitespace-nowrap text-muted-foreground hover:text-foreground',
        'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring',
        'disabled:pointer-events-none disabled:opacity-50',
        'after:absolute after:inset-x-2 after:-bottom-px after:h-0.5 after:rounded-full',
        'data-[state=active]:font-semibold data-[state=active]:text-foreground data-[state=active]:after:bg-primary',
        '[&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*="size-"])]:size-4',
        className,
      )}
      {...props}
    />
  );
}

function TabsContent({ className, ...props }: React.ComponentProps<typeof TabsPrimitive.Content>) {
  return (
    <TabsPrimitive.Content
      data-slot="tabs-content"
      className={cn(
        'focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-ring',
        className,
      )}
      {...props}
    />
  );
}

export { Tabs, TabsContent, TabsList, TabsTrigger };
