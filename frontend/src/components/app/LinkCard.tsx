import type { LucideIcon } from 'lucide-react';
import { useId } from 'react';
import { Link } from 'react-router';

export interface LinkCardProps {
  to: string;
  /** Already translated; the link's name. */
  title: string;
  /** Already translated; read as the link's description. */
  description?: string;
  icon?: LucideIcon;
}

/** A shortcut to a section of the application, e.g. on the start page. */
export function LinkCard({ to, title, description, icon: Icon }: LinkCardProps) {
  const descriptionId = useId();
  const titleId = useId();
  return (
    <Link
      to={to}
      aria-labelledby={titleId}
      aria-describedby={description ? descriptionId : undefined}
      className="group flex min-w-0 items-start gap-3 rounded-lg border bg-card p-4 shadow-e1 transition-colors duration-100 hover:border-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
    >
      {Icon && (
        <span className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary-soft text-primary-soft-foreground">
          <Icon aria-hidden="true" className="size-5" />
        </span>
      )}
      <span className="flex min-w-0 flex-col gap-0.5">
        <span id={titleId} className="text-section break-words text-foreground group-hover:underline">
          {title}
        </span>
        {description && (
          <span id={descriptionId} className="text-help break-words text-muted-foreground">
            {description}
          </span>
        )}
      </span>
    </Link>
  );
}
