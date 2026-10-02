import { cn } from '@/lib/cn';

function Skeleton({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      data-slot="skeleton" // Local edit (design D5): no looping pulse; skeletons fade in after 150 ms so fast loads do not flash.
      className={cn(
        'animate-in rounded-md bg-accent delay-150 duration-150 fade-in-0 fill-mode-backwards',
        className,
      )}
      {...props}
    />
  );
}

export { Skeleton };
