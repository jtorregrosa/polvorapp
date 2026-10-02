import { SearchX } from 'lucide-react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from './Button';
import { EmptyState } from './EmptyState';

export interface FilterBarProps {
  /** `FilterSelect`s and other filters, before the search. */
  filters?: ReactNode;
  /** The list's `SearchField`. */
  search?: ReactNode;
  /** Already translated, e.g. "23 arquebusiers": announced politely whenever it changes. */
  resultText: string;
}

/**
 * The filters of a list in one bar (spec: Data tables, Arquebusier visibility): filters, search and
 * the number of results, which is announced when a filter or the search changes it.
 */
export function FilterBar({ filters, search, resultText }: FilterBarProps) {
  return (
    <div data-slot="filter-bar" className="flex flex-wrap items-end gap-3">
      {filters}
      {search && <div className="w-full max-w-field-long min-w-0 sm:w-auto sm:flex-1">{search}</div>}
      <p role="status" className="ml-auto text-help text-muted-foreground tabular-nums">
        {resultText}
      </p>
    </div>
  );
}

export interface NoMatchesProps {
  /** Already translated, e.g. "No arquebusier matches". */
  title: string;
  description?: string;
  onClear: () => void;
}

/** Shown instead of a list when the filters leave nothing, with an action that clears them. */
export function NoMatches({ title, description, onClear }: NoMatchesProps) {
  const { t } = useTranslation('ui');
  return (
    <EmptyState
      icon={SearchX}
      title={title}
      description={description}
      action={
        <Button
          variant="secondary"
          onClick={() => {
            onClear();
            // This button goes away with the empty state: keep focus on the filters (WCAG 2.4.3).
            document.querySelector<HTMLElement>('[data-slot="filter-bar"] :is(input, select)')?.focus();
          }}
        >
          {t('filters.clear')}
        </Button>
      }
    />
  );
}
