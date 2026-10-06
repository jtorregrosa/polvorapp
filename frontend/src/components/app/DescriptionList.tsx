import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { breakable } from './breakable';

export interface DescriptionItem {
  /** Already translated. */
  term: string;
  /** Already formatted; empty values are shown as "Not given". */
  value: ReactNode;
  /** Identifiers (nationalId, federationId, guide numbers) in the monospaced face. */
  mono?: boolean;
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
