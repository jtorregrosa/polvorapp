import { Ellipsis, type LucideIcon } from 'lucide-react';
import type { ReactNode, Ref } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { BackLink } from './PageHeader';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

export interface MoreAction {
  id: string;
  /** Already translated. */
  label: string;
  icon?: LucideIcon;
  onSelect: () => void;
  /** Shown apart, in the destructive colour; confirm it with `ConfirmDialog`. */
  destructive?: boolean;
  disabled?: boolean;
}

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

function MoreActionItem({ action }: { action: MoreAction }) {
  const Icon = action.icon;
  return (
    // A disabled action stays focusable, so its label (which says why) is still read (WCAG 2.1.1).
    <DropdownMenuItem
      variant={action.destructive ? 'destructive' : 'default'}
      aria-disabled={action.disabled ? true : undefined}
      className={action.disabled ? 'text-muted-foreground' : undefined}
      onSelect={(event) => {
        if (action.disabled) {
          event.preventDefault();
          return;
        }
        action.onSelect();
      }}
    >
      {Icon && <Icon aria-hidden="true" />}
      {action.label}
    </DropdownMenuItem>
  );
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
  const { t } = useTranslation('ui');
  const regular = moreActions.filter((action) => !action.destructive);
  const destructive = moreActions.filter((action) => action.destructive);

  return (
    <header className="flex flex-col gap-group">
      {back && <BackLink {...back} />}
      <div className="flex flex-wrap items-start gap-x-section gap-y-group">
        {/* Never wider than the screen: its actions wrap instead (WCAG 1.4.10). */}
        {media && <div className="max-w-full min-w-0">{media}</div>}
        <div className="flex min-w-0 flex-1 basis-64 flex-col gap-1.5">
          {context && <p className="text-help break-words text-muted-foreground">{context}</p>}
          <h1 className="font-display text-record break-words text-foreground">{name}</h1>
          {statuses && <div className="flex flex-wrap items-center gap-2">{statuses}</div>}
        </div>
        {(actions !== undefined || moreActions.length > 0) && (
          <div className="flex flex-wrap items-center gap-2">
            {actions}
            {moreActions.length > 0 && (
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button ref={moreActionsRef} variant="outline" className="gap-2">
                    <Ellipsis aria-hidden="true" />
                    {t('detail.moreActions')}
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" className="min-w-56">
                  {regular.map((action) => (
                    <MoreActionItem key={action.id} action={action} />
                  ))}
                  {regular.length > 0 && destructive.length > 0 && <DropdownMenuSeparator />}
                  {destructive.map((action) => (
                    <MoreActionItem key={action.id} action={action} />
                  ))}
                </DropdownMenuContent>
              </DropdownMenu>
            )}
          </div>
        )}
      </div>
    </header>
  );
}
