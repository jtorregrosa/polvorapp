import { useId, type ReactNode, type Ref } from 'react';
import { cn } from '@/lib/cn';

export interface SectionCardProps {
  /** Already translated; the section's `h2`. */
  title: string;
  description?: string;
  /** The section's action, e.g. the trigger of its `EditSheet`. */
  action?: ReactNode;
  /** `full` takes the whole row of a `SectionGrid`. */
  span?: 'auto' | 'full';
  /**
   * Makes the section a link target (`#id`) that can take focus, e.g. when an email links to it; the
   * page moves focus there itself.
   */
  anchorId?: string;
  ref?: Ref<HTMLElement>;
  children: ReactNode;
}

/** A titled, read-only section of a detail page with its edit action (spec: Detail pages in read mode). */
export function SectionCard({
  title,
  description,
  action,
  span = 'auto',
  anchorId,
  ref,
  children,
}: SectionCardProps) {
  const titleId = useId();
  return (
    <section
      ref={ref}
      id={anchorId}
      tabIndex={anchorId ? -1 : undefined}
      aria-labelledby={titleId}
      className={cn(
        'flex min-w-0 flex-col gap-group rounded-lg border bg-card p-4 shadow-e1 sm:p-6',
        span === 'full' && 'col-span-full',
      )}
    >
      {/* The action stays at the top end whatever the description's length (audit T4); several actions wrap within half the width. */}
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <h2 id={titleId} className="text-section break-words text-foreground">
            {title}
          </h2>
          {description && <p className="text-help text-muted-foreground">{description}</p>}
        </div>
        {action && <div className="flex max-w-1/2 shrink-0 flex-wrap justify-end gap-2">{action}</div>}
      </div>
      {children}
    </section>
  );
}
