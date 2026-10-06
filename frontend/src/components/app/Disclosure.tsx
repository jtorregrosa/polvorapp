import { ChevronRight } from 'lucide-react';
import type { ReactNode } from 'react';

export interface DisclosureProps {
  /** Already translated; what opening it shows, e.g. "18 comparsas without a slot". */
  summary: ReactNode;
  children: ReactNode;
  defaultOpen?: boolean;
}

/**
 * Details folded under a one-line summary, e.g. a long list inside a notice (native `details`, so
 * the browser gives it its keyboard behaviour and its expanded state to assistive technology).
 */
export function Disclosure({ summary, children, defaultOpen = false }: DisclosureProps) {
  return (
    <details data-slot="disclosure" className="group flex flex-col gap-2" open={defaultOpen || undefined}>
      <summary
        data-slot="disclosure-summary"
        className="flex min-h-8 w-fit cursor-pointer list-none items-center gap-1 rounded-sm font-semibold underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      >
        <ChevronRight
          aria-hidden="true"
          className="size-4 shrink-0 transition-transform duration-100 group-open:rotate-90 motion-reduce:transition-none"
        />
        {summary}
      </summary>
      <div className="pt-1">{children}</div>
    </details>
  );
}
