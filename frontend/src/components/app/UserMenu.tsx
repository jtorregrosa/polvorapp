import { LogOut, UserRound } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

export interface UserMenuProps {
  name: string;
  /** Already translated role. */
  roleLabel: string;
  accountHref: string;
  onSignOut: () => void;
}

/** The signed-in user in the top bar: name and role, a link to the account page and sign-out. */
export function UserMenu({ name, roleLabel, accountHref, onSignOut }: UserMenuProps) {
  const { t } = useTranslation('ui');

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" className="max-w-48 gap-2" aria-label={t('userMenu.label', { name })}>
          <UserRound aria-hidden="true" />
          <span className="hidden truncate sm:inline">{name}</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-56">
        <DropdownMenuLabel className="grid gap-0.5">
          <span className="truncate">{name}</span>
          <span className="text-xs font-normal text-muted-foreground">{roleLabel}</span>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link to={accountHref}>
            <UserRound aria-hidden="true" />
            {t('userMenu.account')}
          </Link>
        </DropdownMenuItem>
        <DropdownMenuItem onSelect={onSignOut}>
          <LogOut aria-hidden="true" />
          {t('userMenu.signOut')}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
