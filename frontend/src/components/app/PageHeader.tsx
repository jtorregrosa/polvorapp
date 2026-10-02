import { ChevronLeft } from 'lucide-react';
import { useEffect, useRef, type ReactNode } from 'react';
import { Link } from 'react-router';

export interface PageHeaderProps {
  /** The page's only h1. */
  title: string;
  description?: string;
  /** Primary and secondary page actions, right-aligned on wide screens. */
  actions?: ReactNode;
  /** Optional link to the parent page. */
  back?: { to: string; label: string };
  /**
   * Moves focus to the title when shown, for a view that replaces the page after an action (e.g.
   * "Check your email"), so screen readers announce it and focus is not lost (WCAG 2.4.3).
   */
  focusOnMount?: boolean;
}

/** The link to the parent page above a page or record title. */
export function BackLink({ to, label }: { to: string; label: string }) {
  return (
    <Link
      to={to}
      className="inline-flex min-h-8 w-fit items-center gap-1 rounded-sm text-label text-muted-foreground hover:text-foreground"
    >
      <ChevronLeft aria-hidden="true" className="size-4" />
      {label}
    </Link>
  );
}

/** Title block of every page template (list, detail, form, dashboard). */
export function PageHeader({ title, description, actions, back, focusOnMount = false }: PageHeaderProps) {
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    if (focusOnMount) {
      heading.current?.focus();
    }
  }, [focusOnMount]);

  return (
    <div className="flex flex-col gap-group sm:flex-row sm:items-end sm:justify-between">
      <div className="flex min-w-0 flex-col gap-1.5">
        {back && <BackLink {...back} />}
        <h1
          ref={heading}
          tabIndex={focusOnMount ? -1 : undefined}
          className="font-display text-page break-words text-foreground outline-none"
        >
          {title}
        </h1>
        {description && <p className="max-w-prose text-body text-muted-foreground">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}
