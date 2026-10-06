import type { ReactNode } from 'react';

export interface KeyFact {
  id: string;
  /** Already translated. */
  label: string;
  value: ReactNode;
  /**
   * How far something has run (e.g. how much of a license's validity has passed), 0 to 1, with an
   * already translated label that says it in words.
   */
  meter?: { value: number; label: string };
}

export interface KeyFactsProps {
  /** Already translated; names the list. */
  label: string;
  items: readonly KeyFact[];
}

/** A native meter (its bar is styled in globals.css), named by a label that says it in words. */
function Meter({ value, label }: { value: number; label: string }) {
  const percent = Math.round(Math.min(1, Math.max(0, value)) * 100);
  return (
    <meter data-slot="meter" aria-label={label} min={0} max={100} value={percent} className="h-1.5 w-full" />
  );
}

/**
 * The few facts that describe a record at a glance, under its header (spec: Registry screens). Each
 * fact is at least 11rem wide and the facts of a row share its width, so the list fits the space it
 * has (a full page or half a card) with no empty filler cells and no word split letter by letter.
 */
export function KeyFacts({ label, items }: KeyFactsProps) {
  return (
    // eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`.
    <ul
      role="list"
      aria-label={label}
      className="flex flex-wrap gap-px overflow-hidden rounded-lg border bg-border shadow-e1"
    >
      {items.map((item) => (
        <li key={item.id} className="flex min-w-0 flex-1 basis-44 flex-col gap-1 bg-card px-4 py-3">
          <span className="text-help text-muted-foreground">{item.label}</span>
          <span className="text-section break-words text-foreground tabular-nums">{item.value}</span>
          {item.meter && <Meter {...item.meter} />}
        </li>
      ))}
    </ul>
  );
}
