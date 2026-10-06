import { Fragment, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';

export interface DescriptionItem {
  /** Already translated. */
  term: string;
  /** Already formatted; empty values are shown as "Not given". */
  value: ReactNode;
  /** Identifiers (nationalId, federationId, guide numbers) in the monospaced face. */
  mono?: boolean;
}

/**
 * An email address that may break after "@" and after each "." instead of mid-word ("polvorapp.e /
 * xample") in a narrow column (UI audit T10); any other value as it is.
 */
function breakable(value: ReactNode): ReactNode {
  if (typeof value !== 'string' || !/^[^\s@]+@[^\s@]+$/.test(value)) return value;
  return value.split(/(?<=[@.])/).map((part, index) => (
    <Fragment key={index}>
      {index > 0 && <wbr />}
      {part}
    </Fragment>
  ));
}

/** Read-only values of a record, term above value, "Not given" when empty (spec: Detail pages in read mode). */
export function DescriptionList({ items }: { items: readonly DescriptionItem[] }) {
  const { t } = useTranslation('ui');
  return (
    <dl className="grid gap-x-group gap-y-3 sm:grid-cols-2">
      {items.map((item) => {
        const empty = item.value === null || item.value === undefined || item.value === '';
        return (
          <div key={item.term} className="flex min-w-0 flex-col gap-0.5">
            <dt className="text-help text-muted-foreground">{item.term}</dt>
            <dd className={cn('break-words text-foreground', empty && 'text-muted-foreground')}>
              {empty ? (
                t('detail.notGiven')
              ) : (
                <span className={cn(item.mono && 'font-mono text-id')}>{breakable(item.value)}</span>
              )}
            </dd>
          </div>
        );
      })}
    </dl>
  );
}
