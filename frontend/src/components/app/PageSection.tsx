import { useId, type ReactNode } from 'react';

export interface PageSectionProps {
  title: string;
  description?: string;
  /** Controls next to the heading, e.g. an "Add" link. */
  actions?: ReactNode;
  children: ReactNode;
}

/**
 * A titled part of a page that is not a group of form fields, such as a table or an action (use
 * {@link FormSection} for fields): a region with an `h2`, so headings navigation reaches it. Looks
 * like a {@link FormSection}, so the two can sit together on a detail page.
 */
export function PageSection({ title, description, actions, children }: PageSectionProps) {
  const headingId = useId();
  const descriptionId = useId();

  return (
    <section
      aria-labelledby={headingId}
      aria-describedby={description ? descriptionId : undefined}
      className="flex min-w-0 flex-col gap-4 rounded-lg border bg-card p-4 sm:p-6"
    >
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="flex min-w-0 flex-col gap-1">
          <h2 id={headingId} className="text-base font-semibold text-foreground">
            {title}
          </h2>
          {description && (
            <p id={descriptionId} className="text-sm text-muted-foreground">
              {description}
            </p>
          )}
        </div>
        {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
      </div>
      {children}
    </section>
  );
}
