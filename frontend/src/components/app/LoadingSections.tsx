import { Skeleton } from '@/components/ui/skeleton';

export interface LoadingSectionsProps {
  /** How many section placeholders to draw. */
  count?: number;
}

/**
 * Placeholders shaped like the sections a page is loading (heading, a line, a table or a chart),
 * hidden from assistive technology: the page says it is loading in its own status region.
 */
export function LoadingSections({ count = 2 }: LoadingSectionsProps) {
  return (
    <div aria-hidden="true" data-slot="loading-sections" className="grid gap-section lg:grid-cols-2">
      {Array.from({ length: count }, (_, index) => (
        <div key={index} className="flex flex-col gap-3 rounded-lg border bg-card p-4 shadow-e1 sm:p-6">
          <Skeleton className="h-5 w-1/3" />
          <Skeleton className="h-4 w-2/3" />
          <Skeleton className="h-48 w-full" />
        </div>
      ))}
    </div>
  );
}
