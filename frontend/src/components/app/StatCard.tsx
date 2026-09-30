import { cn } from 'cn';
import { useId, type ReactNode } from 'react';
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
    <div data-stat-card="" className={cn('relative', classes)}>
      <Link
        to={to}
        aria-describedby={description ? descriptionId : undefined}
        className="flex flex-col gap-1 rounded-sm after:absolute after:inset-0"
      >
        {figure}
      </Link>
      {context}
    </div>
  );
}
