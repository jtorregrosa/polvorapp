import type { LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';

export interface EmptyStateProps {
  title: string;
  description?: string;
  icon?: LucideIcon;
  /** Usually the action that fills the empty view (e.g. "Add arquebusier"). */
  action?: ReactNode;
  headingLevel?: 2 | 3;
}

/** Shown instead of an empty list, table or panel (design guide: empty states). */
export function EmptyState({ title, description, icon: Icon, action, headingLevel = 2 }: EmptyStateProps) {
  const Heading = headingLevel === 2 ? 'h2' : 'h3';

  return (
    <div className="flex flex-col items-center gap-3 rounded-lg border border-dashed bg-card px-6 py-10 text-center">
      {Icon && (
        <span className="flex size-10 items-center justify-center rounded-full bg-muted text-muted-foreground">
          <Icon aria-hidden="true" className="size-5" />
        </span>
      )}
      <Heading className="text-base font-semibold text-foreground">{title}</Heading>
      {description && <p className="max-w-prose text-sm text-muted-foreground">{description}</p>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  );
}
