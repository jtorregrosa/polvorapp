import { CircleX } from 'lucide-react';
import { useId, type Ref } from 'react';

export interface ErrorSummaryItem {
  /** Already translated, e.g. "Birth date: This field is required". */
  text: string;
  /** Id of the control to focus; an error that belongs to no field has none. */
  fieldId?: string;
}

export interface ErrorSummaryProps {
  /** Already translated, e.g. "There is a problem". */
  title: string;
  items: readonly ErrorSummaryItem[];
  ref?: Ref<HTMLDivElement>;
}

/** The control to focus for a field: the chosen or first option of a radio group, or itself. */
function focusTarget(field: HTMLElement): HTMLElement {
  if (field.matches('[role="radiogroup"]')) {
    return (
      field.querySelector<HTMLElement>('[role="radio"][aria-checked="true"]') ??
      field.querySelector<HTMLElement>('[role="radio"]') ??
      field
    );
  }
  if (field.matches('input, select, textarea, button, [tabindex]')) return field;
  return field.querySelector<HTMLElement>('input, select, textarea, button, [tabindex]') ?? field;
}

/**
 * The problems of a submitted form, at its top (spec: Form fields and validation messages): a
 * title, then one link per error that moves focus to its field and scrolls the field, label
 * included, into view (below the top bar and above the action bar, through the page's scroll
 * padding). {@link Form} renders it and focuses it after a failed submission, which reads its title;
 * it is not a live region, so fixing a field does not announce the whole summary again. Each error
 * is also repeated at its field.
 */
export function ErrorSummary({ title, items, ref }: ErrorSummaryProps) {
  const titleId = useId();

  return (
    <div
      ref={ref}
      role="group"
      aria-labelledby={titleId}
      tabIndex={-1}
      data-slot="error-summary"
      className="flex gap-3 rounded-lg border-2 border-destructive bg-card p-4 text-body focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
    >
      <CircleX aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />
      <div className="flex min-w-0 flex-col gap-2">
        <h2 id={titleId} className="text-section text-foreground">
          {title}
        </h2>
        <ul className="flex flex-col gap-1">
          {items.map((item) => (
            <li key={`${item.fieldId ?? 'form'}:${item.text}`} className="break-words">
              {item.fieldId ? (
                <a
                  href={`#${item.fieldId}`}
                  className="font-semibold text-destructive underline underline-offset-4 hover:no-underline"
                  onClick={(event) => {
                    const field = document.getElementById(item.fieldId ?? '');
                    if (!field) return; // Let the browser follow the anchor.
                    event.preventDefault();
                    focusTarget(field).focus({ preventScroll: true });
                    (field.closest('[data-slot="form-item"]') ?? field).scrollIntoView({ block: 'start' });
                  }}
                >
                  {item.text}
                </a>
              ) : (
                <span className="font-semibold text-destructive">{item.text}</span>
              )}
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
