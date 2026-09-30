import { ChevronLeft } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router';

export interface PageHeaderProps {
  /** The page's only h1. */
  title: string;
  description?: string;
  /** Primary and secondary page actions, right-aligned on wide screens. */
  actions?: ReactNode;
  /** Optional link to the parent page. */
  back?: { to: string; label: string };
}

/** Title block of every page template (list, detail, form, dashboard). */
export function PageHeader({ title, description, actions, back }: PageHeaderProps) {
  return (
    <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div className="flex min-w-0 flex-col gap-1">
        {back && (
          <Link
            to={back.to}
            className="inline-flex min-h-6 w-fit items-center gap-1 rounded-sm text-sm text-muted-foreground hover:text-foreground"
          >
            <ChevronLeft aria-hidden="true" className="size-4" />
            {back.label}
          </Link>
        )}
        <h1 className="text-2xl font-semibold tracking-tight text-foreground">{title}</h1>
        {description && <p className="text-sm text-muted-foreground">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}
