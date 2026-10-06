import { Ellipsis, type LucideIcon } from 'lucide-react';
import type { Ref } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
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
 * A "More actions" menu for rarely used and destructive actions (spec: Action hierarchy), the
 * destructive ones set apart at the end. Used by `RecordHeader` and `PageHeader`.
 */
export function MoreActionsMenu({
  actions,
  triggerRef,
}: {
  actions: readonly MoreAction[];
  /** The menu's button, e.g. to return focus to it after a confirmation it opened. */
  triggerRef?: Ref<HTMLButtonElement>;
}) {
  const { t } = useTranslation('ui');
  const regular = actions.filter((action) => !action.destructive);
  const destructive = actions.filter((action) => action.destructive);
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button ref={triggerRef} variant="outline" className="gap-2">
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
  );
}
