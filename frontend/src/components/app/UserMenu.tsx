import { LogOut, Monitor, Moon, Sun, UserRound, type LucideIcon } from 'lucide-react';
import { useId, type Ref } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { matchLanguage, rememberLanguage, SUPPORTED_LANGUAGES, type Language } from '@/i18n/config';
import { isThemePreference, THEME_PREFERENCES, type ThemePreference } from '@/theme/config';
import { useTheme } from '@/theme/ThemeProvider';

const THEME_ICONS: Record<ThemePreference, LucideIcon> = { light: Sun, dark: Moon, system: Monitor };

export interface UserMenuProps {
  name: string;
  /** Already translated role. */
  roleLabel: string;
  accountHref: string;
  onSignOut: () => void;
  /** Called after the language changes, e.g. to save it as the user's preferred language. */
  onLanguageChange?: (language: Language) => void;
  /** The menu's button, e.g. for a dialog opened from the menu to return focus to (WCAG 2.4.3). */
  triggerRef?: Ref<HTMLButtonElement>;
}

/** Up to two initials of a name, for the avatar. */
function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part.charAt(0).toLocaleUpperCase())
    .join('');
}

/**
 * The signed-in user in the top bar (platform spec: Application shell): name and role, the language
 * and theme switchers as radio groups that change the UI at once, the account page and sign-out.
 */
export function UserMenu({
  name,
  roleLabel,
  accountHref,
  onSignOut,
  onLanguageChange,
  triggerRef,
}: UserMenuProps) {
  const { t, i18n } = useTranslation('ui');
  const { t: tCommon } = useTranslation();
  const { preference, setPreference } = useTheme();
  const languageLabel = useId();
  const themeLabel = useId();

  const changeLanguage = (value: string): void => {
    const language = matchLanguage(value);
    if (!language || language === i18n.resolvedLanguage) return;
    rememberLanguage(language);
    void i18n.changeLanguage(language);
    onLanguageChange?.(language);
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          ref={triggerRef}
          variant="ghost"
          className="max-w-56 gap-2 px-2"
          aria-label={t('userMenu.label', { name })}
        >
          <span
            aria-hidden="true"
            className="flex size-8 shrink-0 items-center justify-center rounded-full bg-primary-soft font-display text-label text-primary-soft-foreground"
          >
            {initials(name)}
          </span>
          <span className="hidden truncate sm:inline">{name}</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-64">
        <DropdownMenuLabel className="grid gap-0.5">
          <span className="break-words">{name}</span>
          <span className="text-help font-normal text-muted-foreground">{roleLabel}</span>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuLabel id={languageLabel} className="text-help font-normal text-muted-foreground">
          {tCommon('shell.language.label')}
        </DropdownMenuLabel>
        <DropdownMenuRadioGroup
          aria-labelledby={languageLabel}
          value={i18n.resolvedLanguage ?? i18n.language}
          onValueChange={changeLanguage}
        >
          {SUPPORTED_LANGUAGES.map((language) => (
            <DropdownMenuRadioItem key={language} value={language} lang={language}>
              {tCommon(`shell.language.options.${language}`)}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
        <DropdownMenuSeparator />
        <DropdownMenuLabel id={themeLabel} className="text-help font-normal text-muted-foreground">
          {t('theme.label')}
        </DropdownMenuLabel>
        <DropdownMenuRadioGroup
          aria-labelledby={themeLabel}
          value={preference}
          onValueChange={(value) => {
            if (isThemePreference(value)) setPreference(value);
          }}
        >
          {THEME_PREFERENCES.map((option) => {
            const Icon = THEME_ICONS[option];
            return (
              <DropdownMenuRadioItem key={option} value={option}>
                <Icon aria-hidden="true" />
                {t(`theme.${option}`)}
              </DropdownMenuRadioItem>
            );
          })}
        </DropdownMenuRadioGroup>
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
