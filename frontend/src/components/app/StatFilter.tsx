import { Check } from 'lucide-react';
import { useId } from 'react';
import { cn } from '@/lib/cn';

export interface StatFilterItem {
  id: string;
  /** Already translated, e.g. "Expired license". */
  label: string;
  /** Already formatted for the active language. */
  count: number | string;
  /** Already translated; read as the button's description. */
  hint?: string;
  /** Marks counters that need attention; the label always says what they count. */
  tone?: 'neutral' | 'warning' | 'destructive';
  pressed: boolean;
  onPressedChange: (pressed: boolean) => void;
}

export interface StatFilterProps {
  /** Already translated; names the group of counters. */
  label: string;
  items: readonly StatFilterItem[];
}

const TONES: Record<NonNullable<StatFilterItem['tone']>, string> = {
  neutral: 'border-l-input',
  warning: 'border-l-warning',
  destructive: 'border-l-destructive',
};

/**
 * Counters above a list that are also filters (spec: Arquebusier visibility): each is a toggle
 * button with its figure and label, pressed while its filter is on. The page combines the
 * filters and announces the result count (see `FilterBar`). A pressed counter shows a check and a
 * ring besides its tint, so the state never depends on colour alone (WCAG 1.4.1, 1.4.11).
 */
/**
 * Columns by number of counters, so no row ends with a lone card (audit T6): six make 2×3, 3×2 or
 * one row; five keep one row on wide screens.
 */
const COLUMNS: Readonly<Record<number, string>> = {
  1: 'grid-cols-1',
  2: 'grid-cols-2',
  3: 'grid-cols-1 sm:grid-cols-3',
  4: 'grid-cols-2 lg:grid-cols-4',
  5: 'grid-cols-2 sm:grid-cols-3 lg:grid-cols-5',
  6: 'grid-cols-2 sm:grid-cols-3 xl:grid-cols-6',
};

export function StatFilter({ label, items }: StatFilterProps) {
  const baseId = useId();
  return (
    <div
      role="group"
      aria-label={label}
      data-slot="stat-filter"
      className={cn('grid gap-2', COLUMNS[items.length] ?? 'grid-cols-2 sm:grid-cols-3 lg:grid-cols-4')}
    >
      {items.map((item) => {
        const id = `${baseId}-${item.id}`;
        const tone = TONES[item.tone ?? 'neutral'];
        return (
          <button
            key={item.id}
            type="button"
            aria-pressed={item.pressed}
            aria-labelledby={`${id}-count ${id}-label`}
            aria-describedby={item.hint ? `${id}-hint` : undefined}
            onClick={() => {
              item.onPressedChange(!item.pressed);
            }}
            className={cn(
              'flex min-h-control min-w-0 flex-col items-start gap-0.5 rounded-lg border border-l-4 bg-card px-3 py-2.5 text-start shadow-e1',
              'transition-colors duration-100 hover:bg-surface-2',
              'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
              'aria-pressed:border-primary aria-pressed:bg-primary-soft aria-pressed:ring-2 aria-pressed:ring-primary',
              tone,
            )}
          >
            <span className="flex w-full items-start justify-between gap-2">
              <span id={`${id}-count`} className="font-display text-figure text-foreground tabular-nums">
                {item.count}
              </span>
              {item.pressed && (
                <Check aria-hidden="true" data-pressed-mark="" className="size-4 shrink-0 text-primary" />
              )}
            </span>
            <span id={`${id}-label`} className="text-label break-words text-foreground">
              {item.label}
            </span>
            {item.hint && (
              <span id={`${id}-hint`} className="text-help break-words text-muted-foreground">
                {item.hint}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}
