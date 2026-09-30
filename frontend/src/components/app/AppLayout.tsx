import type { LucideIcon } from 'lucide-react';
import { useEffect, useRef, type ReactNode, type RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from 'react-router';
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarHeader,
  SidebarInset,
  SidebarMenu,
  SidebarMenuBadge,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarTrigger,
  useSidebar,
} from '@/components/ui/sidebar';
import { Breadcrumbs } from './Breadcrumbs';
import { isCurrentPath } from './navigation-match';
import { LanguageSwitcher } from './LanguageSwitcher';
import { PolvorAppMark } from './PolvorAppMark';
import { ThemeSwitcher } from './ThemeSwitcher';

export interface NavigationItem {
  to: string;
  /** Already translated label. */
  label: string;
  icon: LucideIcon;
  /** Optional counter, e.g. pending warnings. */
  count?: number;
}

export interface AppLayoutProps {
  navigation: readonly NavigationItem[];
  /** Shown at the bottom of the sidebar (API version). */
  sidebarFooter?: ReactNode;
  mainRef?: RefObject<HTMLElement | null>;
  children: ReactNode;
}

function NavigationMenu({ items }: { items: readonly NavigationItem[] }) {
  const { t } = useTranslation('ui');
  const { pathname } = useLocation();
  const { isMobile, setOpenMobile } = useSidebar();

  return (
    <nav aria-label={t('nav.label')}>
      <SidebarMenu>
        {items.map(({ to, label, icon: Icon, count }) => {
          const current = isCurrentPath(pathname, to);
          return (
            <SidebarMenuItem key={to}>
              <SidebarMenuButton
                asChild
                isActive={current}
                // Current page: accent bar and weight, not background alone (WCAG 1.4.1).
                className="relative before:absolute before:inset-y-1.5 before:left-0 before:w-1 before:rounded-full data-[active=true]:font-semibold data-[active=true]:before:bg-sidebar-primary"
              >
                <Link
                  to={to}
                  aria-current={current ? 'page' : undefined}
                  onClick={() => {
                    if (isMobile) setOpenMobile(false);
                  }}
                >
                  <Icon aria-hidden="true" />
                  <span>{label}</span>
                </Link>
              </SidebarMenuButton>
              {count !== undefined && count > 0 && <SidebarMenuBadge>{count}</SidebarMenuBadge>}
            </SidebarMenuItem>
          );
        })}
      </SidebarMenu>
    </nav>
  );
}

/**
 * Opens the navigation drawer on small screens. The drawer opens from state (not a Radix
 * trigger), so focus is returned here explicitly when it closes (WCAG 2.4.3).
 */
function NavigationTrigger() {
  const { open, openMobile, isMobile } = useSidebar();
  const trigger = useRef<HTMLButtonElement>(null);
  const wasOpen = useRef(openMobile);

  useEffect(() => {
    if (wasOpen.current && !openMobile) {
      trigger.current?.focus();
    }
    wasOpen.current = openMobile;
  }, [openMobile]);

  return <SidebarTrigger ref={trigger} aria-expanded={isMobile ? openMobile : open} className="size-9" />;
}

/**
 * Application layout (platform spec: Application shell): skip link, sidebar with the PolvorApp
 * mark, navigation and footer; top bar with breadcrumbs and switchers; main content. On small
 * screens the sidebar becomes a drawer opened from the top bar.
 */
export function AppLayout({ navigation, sidebarFooter, mainRef, children }: AppLayoutProps) {
  const { t } = useTranslation();

  return (
    <SidebarProvider>
      <a
        href="#main"
        className="sr-only z-50 rounded-md bg-background px-4 py-2 text-foreground focus:not-sr-only focus:fixed focus:top-4 focus:left-4"
      >
        {t('shell.skipToContent')}
      </a>
      <Sidebar>
        <SidebarHeader>
          <Link
            to="/"
            className="flex items-center gap-2 rounded-md p-2 text-lg font-semibold text-sidebar-foreground"
          >
            <PolvorAppMark />
            <span>{t('app.name')}</span>
          </Link>
        </SidebarHeader>
        <SidebarContent className="px-2">
          <NavigationMenu items={navigation} />
        </SidebarContent>
        {sidebarFooter && (
          <SidebarFooter className="px-4 text-xs text-muted-foreground">{sidebarFooter}</SidebarFooter>
        )}
      </Sidebar>
      <div className="flex min-h-svh min-w-0 flex-1 flex-col">
        <header className="flex min-h-14 shrink-0 flex-wrap items-center gap-2 border-b bg-background px-3 py-2 sm:px-4">
          <NavigationTrigger />
          <div className="min-w-0 flex-1">
            <Breadcrumbs />
          </div>
          <LanguageSwitcher />
          <ThemeSwitcher />
        </header>
        <SidebarInset id="main" ref={mainRef} tabIndex={-1} className="px-4 py-6 outline-none sm:px-6">
          <div className="mx-auto w-full max-w-6xl">{children}</div>
        </SidebarInset>
      </div>
    </SidebarProvider>
  );
}
