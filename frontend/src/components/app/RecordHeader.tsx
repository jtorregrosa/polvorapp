import type { ReactNode, Ref } from 'react';
import { MoreActionsMenu, type MoreAction } from './MoreActionsMenu';
import { BackLink } from './PageHeader';

export type { MoreAction };

export interface RecordHeaderProps {
  /** The record's photo or mark. */
  media?: ReactNode;
  /** Already translated line above the name, e.g. the comparsa and its side. */
  context?: ReactNode;
  /** The record's name: the page's only `h1`. */
  name: string;
  /** `StatusBadge`s of the record. */
  statuses?: ReactNode;
  /** Frequent actions, as buttons. */
  actions?: ReactNode;
  /** Rarely used and destructive actions, in a "More actions" menu (spec: Action hierarchy). */
  moreActions?: readonly MoreAction[];
  /** The link to the parent page above the header, e.g. to the list. */
  back?: { to: string; label: string };
  /** The "More actions" button, e.g. to return focus to it after a confirmation it opened. */
  moreActionsRef?: Ref<HTMLButtonElement>;
}

/**
 * The top of a detail page (spec: Page templates, detail): the record's photo, a context line, its
 * name as the page title, its statuses, the frequent actions and a "More actions" menu where
 * destructive actions are set apart.
 */
export function RecordHeader({
  media,
  context,
  name,
  statuses,
  actions,
  moreActions = [],
  back,
  moreActionsRef,
}: RecordHeaderProps) {
  return (
    <header className="flex flex-col gap-group">
      {back && <BackLink {...back} />}
      <div className="flex flex-wrap items-start gap-x-section gap-y-group">
        {/* A small photo keeps the name beside it on phones; a wide one (a logo) moves above it. */}
        <div className="flex min-w-0 flex-1 basis-80 flex-wrap items-start gap-4">
          {/* Never wider than the screen: its actions wrap instead (WCAG 1.4.10). */}
          {media && <div className="max-w-full min-w-0 shrink-0">{media}</div>}
          <div className="flex min-w-0 flex-1 basis-48 flex-col gap-1.5">
            {context && <p className="text-help break-words text-muted-foreground">{context}</p>}
            <h1 className="font-display text-record break-words text-foreground">{name}</h1>
            {statuses && <div className="flex flex-wrap items-center gap-2">{statuses}</div>}
          </div>
        </div>
        {(actions !== undefined || moreActions.length > 0) && (
          <div className="flex flex-wrap items-center gap-2">
            {actions}
            {moreActions.length > 0 && <MoreActionsMenu actions={moreActions} triggerRef={moreActionsRef} />}
          </div>
        )}
      </div>
    </header>
  );
}
