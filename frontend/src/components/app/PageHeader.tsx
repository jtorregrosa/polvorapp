import { ChevronLeft } from 'lucide-react';
import { useEffect, useRef, type ReactNode, type Ref } from 'react';
import { Link } from 'react-router';
import { MoreActionsMenu, type MoreAction } from './MoreActionsMenu';

export interface PageHeaderProps {
  /** The page's only h1. */
  title: string;
  description?: string;
  /** `StatusBadge`s of what the page shows (e.g. an edition), beside the title. */
  statuses?: ReactNode;
  /** Primary and secondary page actions, right-aligned on wide screens. */
  actions?: ReactNode;
  /** Rarely used actions, after the others in a "More actions" menu (spec: Action hierarchy). */
  moreActions?: readonly MoreAction[];
  /** The "More actions" button, e.g. to return focus to it after a confirmation it opened. */
  moreActionsRef?: Ref<HTMLButtonElement>;
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
export function PageHeader({
  title,
  description,
  statuses,
  actions,
  moreActions = [],
  moreActionsRef,
  back,
  focusOnMount = false,
}: PageHeaderProps) {
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    if (focusOnMount) {
      heading.current?.focus();
    }
  }, [focusOnMount]);

  return (
    // The title takes at least 16rem before the actions wrap below it, so it never breaks letter by letter.
    <div className="flex flex-wrap items-end justify-between gap-group">
      <div className="flex min-w-0 flex-1 basis-64 flex-col gap-1.5">
        {back && <BackLink {...back} />}
        <h1
          ref={heading}
          tabIndex={focusOnMount ? -1 : undefined}
          className="font-display text-page break-words text-foreground outline-none"
        >
          {title}
        </h1>
        {statuses && <div className="flex flex-wrap items-center gap-2">{statuses}</div>}
        {description && <p className="max-w-prose text-body text-muted-foreground">{description}</p>}
      </div>
      {(Boolean(actions) || moreActions.length > 0) && (
        <div className="flex max-w-full flex-wrap items-center gap-2">
          {actions}
          {moreActions.length > 0 && <MoreActionsMenu actions={moreActions} triggerRef={moreActionsRef} />}
        </div>
      )}
    </div>
  );
}
