import { cn } from '@/lib/cn';
import { useId, type ReactNode } from 'react';
import { ChevronRight } from 'lucide-react';
import { Link } from 'react-router';

export interface StatCardProps {
  label: string;
  /** Already formatted for the active language (see useFormatters). */
  value: ReactNode;
  /** Context for the figure; exposed as the card's description, not as part of a link's name. */
  description?: string;
  /** Makes the whole card a link to the detail view. */
  to?: string;
  className?: string;
}

/** A key figure on a dashboard (design guide: dashboard template). */
export function StatCard({ label, value, description, to, className }: StatCardProps) {
  const descriptionId = useId();
  const classes = cn(
    'flex flex-col gap-1 rounded-lg border bg-card p-4 text-card-foreground',
    to && 'transition-colors hover:border-primary',
    className,
  );
  const figure = (
    <>
      <span className="text-sm text-muted-foreground">{label}</span>{' '}
      <span className="text-2xl font-semibold text-foreground tabular-nums">{value}</span>
    </>
  );
  const context = description && (
    <span id={descriptionId} className="text-xs text-muted-foreground">
      {description}
    </span>
  );

  if (!to) {
    return (
      <div data-stat-card="" className={classes}>
        {figure}
        {context}
      </div>
    );
  }

  return (
    // The whole card is the target, so it shows the focus ring and, besides the border colour, a
    // chevron and an underlined label say it is a link (WCAG 1.4.1, 2.4.7).
    <div
      data-stat-card=""
      className={cn(
        'group relative has-[a:focus-visible]:outline-2 has-[a:focus-visible]:outline-offset-2 has-[a:focus-visible]:outline-ring',
        classes,
      )}
    >
      <ChevronRight
        aria-hidden="true"
        className="absolute top-4 right-4 size-4 text-muted-foreground transition-transform duration-100 group-hover:translate-x-0.5"
      />
      <Link
        to={to}
        aria-describedby={description ? descriptionId : undefined}
        className="flex flex-col gap-1 rounded-sm pr-6 outline-none after:absolute after:inset-0 group-hover:[&>span:first-child]:underline"
      >
        {figure}
      </Link>
      {context}
    </div>
  );
}
