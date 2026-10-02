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
import { PolvorAppMark } from './PolvorAppMark';
import { SaveNoticeProvider } from './SaveNotice';

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
  /** End of the top bar: the signed-in user's menu, which holds the language and theme switchers. */
  userMenu?: ReactNode;
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
                // Current page: an ember bar, weight and icon, not background alone (WCAG 1.4.1).
                className="relative min-h-9 text-sidebar-muted-foreground before:absolute before:inset-y-1.5 before:left-0 before:w-1 before:rounded-full hover:text-sidebar-foreground data-[active=true]:font-semibold data-[active=true]:text-sidebar-foreground data-[active=true]:before:bg-sidebar-primary data-[active=true]:[&>svg]:text-sidebar-primary"
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

  return (
    <SidebarTrigger ref={trigger} aria-expanded={isMobile ? openMobile : open} className="size-control" />
  );
}

/**
 * Application layout (platform spec: Application shell): skip link; the night sidebar with the
 * PolvorApp mark, the navigation and the footer; a sticky top bar with the breadcrumbs and the
 * user menu; the main content, up to 1680 px beside the sidebar, with the top bar aligned to it.
 * On small screens the sidebar becomes a drawer opened from the top bar. It mounts the page's
 * `SaveNotice` region.
 */
export function AppLayout({ navigation, sidebarFooter, userMenu, mainRef, children }: AppLayoutProps) {
  const { t } = useTranslation();
  const topBar = useRef<HTMLElement>(null);

  // The top bar grows when the breadcrumbs wrap: keep its real height as scroll padding, so a
  // focused control never sits under it (SC 2.4.11).
  useEffect(() => {
    const element = topBar.current;
    if (!element) return;
    const root = document.documentElement;
    const observer = new ResizeObserver(() => {
      root.style.setProperty('--topbar-height', `${String(element.offsetHeight)}px`);
    });
    observer.observe(element);
    return () => {
      observer.disconnect();
      root.style.removeProperty('--topbar-height');
    };
  }, []);

  return (
    <SaveNoticeProvider>
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
              className="flex items-center gap-2.5 rounded-md p-2 font-display text-section font-bold text-sidebar-foreground focus-visible:outline-2 focus-visible:outline-sidebar-ring"
            >
              <PolvorAppMark />
              <span>{t('app.name')}</span>
            </Link>
          </SidebarHeader>
          <SidebarContent className="px-2">
            <NavigationMenu items={navigation} />
          </SidebarContent>
          {sidebarFooter && (
            <SidebarFooter className="px-4 text-xs text-sidebar-muted-foreground">
              {sidebarFooter}
            </SidebarFooter>
          )}
        </Sidebar>
        <div className="flex min-h-svh min-w-0 flex-1 flex-col">
          <header ref={topBar} className="sticky top-0 z-20 shrink-0 border-b bg-card px-gutter">
            <div className="flex min-h-topbar w-full max-w-page items-center gap-2 py-2">
              <NavigationTrigger />
              <div className="min-w-0 flex-1">
                <Breadcrumbs />
              </div>
              {userMenu}
            </div>
          </header>
          <SidebarInset
            id="main"
            ref={mainRef}
            tabIndex={-1}
            className="px-gutter pt-section pb-region outline-none"
          >
            <div className="flex w-full max-w-page flex-col gap-section">{children}</div>
          </SidebarInset>
        </div>
      </SidebarProvider>
    </SaveNoticeProvider>
  );
}
