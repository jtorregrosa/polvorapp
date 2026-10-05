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
import { currentNavigationTarget, isCurrentPath } from './navigation-match';
import { PolvorAppMark } from './PolvorAppMark';
import { SaveNoticeProvider } from './SaveNotice';

interface NavigationLink {
  to: string;
  /** Already translated label. */
  label: string;
  icon: LucideIcon;
  /** Other paths the entry owns, a `*` segment matching any one segment (e.g. an edition's orders). */
  matches?: readonly string[];
}

/**
 * An optional counter, shown only above zero, with what it means, already translated (e.g. "5 with
 * warnings"). The label becomes part of the link's accessible name while the badge is hidden from
 * assistive technology, so the two always come together.
 */
type NavigationCount = { count?: undefined; countLabel?: undefined } | { count: number; countLabel: string };

export type NavigationItem = NavigationLink & NavigationCount;

/** A link card in the sidebar, under the mark: e.g. a FiringChief's comparsa with its logo. */
export interface SidebarCard {
  to: string;
  /** Already translated or proper name. */
  label: string;
  /** Shown before the label, e.g. a `ComparsaLogo`; decorative. */
  media: ReactNode;
}

export interface AppLayoutProps {
  navigation: readonly NavigationItem[];
  /** Cards under the mark, in this order (platform spec: Application shell); none when empty. */
  sidebarCards?: readonly SidebarCard[];
  /** Shown at the bottom of the sidebar (API version). */
  sidebarFooter?: ReactNode;
  /** End of the top bar: the signed-in user's menu, which holds the language and theme switchers. */
  userMenu?: ReactNode;
  mainRef?: RefObject<HTMLElement | null>;
  children: ReactNode;
}

/** Closes the navigation drawer after a destination is chosen on a small screen. */
function useCloseDrawer(): () => void {
  const { isMobile, setOpenMobile } = useSidebar();
  return () => {
    if (isMobile) setOpenMobile(false);
  };
}

function NavigationMenu({ items }: { items: readonly NavigationItem[] }) {
  const { t } = useTranslation('ui');
  const { pathname } = useLocation();
  const closeDrawer = useCloseDrawer();
  const currentTarget = currentNavigationTarget(pathname, items);

  return (
    <nav aria-label={t('nav.label')}>
      <SidebarMenu>
        {items.map(({ to, label, icon: Icon, count, countLabel }) => {
          const current = to === currentTarget;
          const counted = count !== undefined && count > 0;
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
                  // The name starts with the visible label (WCAG 2.5.3) and says what the badge counts;
                  // an aria-label is exact, where hidden text gets a space before the comma.
                  aria-label={counted ? `${label}, ${countLabel}` : undefined}
                  onClick={closeDrawer}
                >
                  <Icon aria-hidden="true" />
                  <span className="break-words">{label}</span>
                </Link>
              </SidebarMenuButton>
              {counted && <SidebarMenuBadge aria-hidden="true">{count}</SidebarMenuBadge>}
            </SidebarMenuItem>
          );
        })}
      </SidebarMenu>
    </nav>
  );
}

/**
 * The sidebar cards (add-comparsa-logos D8): links on the night surface with a visible focus ring
 * and a 44 px target. A long name wraps over as many lines as it needs, never clipped (WCAG
 * 1.4.10, 1.4.12). The open comparsa is marked like the current navigation item: weight and the
 * ember bar, not colour alone. Choosing a card closes the mobile drawer, as the navigation does.
 */
function SidebarCards({ cards }: { cards: readonly SidebarCard[] }) {
  const { t } = useTranslation('ui');
  const { pathname } = useLocation();
  const closeDrawer = useCloseDrawer();

  return (
    <nav aria-label={t('nav.comparsas')}>
      <ul className="flex flex-col gap-1">
        {cards.map(({ to, label, media }) => {
          const current = isCurrentPath(pathname, to);
          return (
            <li key={to}>
              <Link
                to={to}
                aria-current={current ? 'page' : undefined}
                className="relative flex min-h-11 items-center gap-2.5 rounded-md bg-sidebar-accent py-1.5 pr-2 pl-3 text-label text-sidebar-foreground before:absolute before:inset-y-2 before:left-0 before:w-1 before:rounded-full hover:bg-sidebar-border focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sidebar-ring aria-[current=page]:font-semibold aria-[current=page]:before:bg-sidebar-primary"
                onClick={closeDrawer}
              >
                {media}
                <span className="min-w-0 wrap-break-word">{label}</span>
              </Link>
            </li>
          );
        })}
      </ul>
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
 * PolvorApp mark, optional link cards (a FiringChief's comparsas), the navigation and the footer;
 * a sticky top bar with the breadcrumbs and the user menu; the main content, up to 1680 px beside
 * the sidebar, with the top bar aligned to it.
 * On small screens the sidebar becomes a drawer opened from the top bar. It mounts the page's
 * `SaveNotice` region.
 */
export function AppLayout({
  navigation,
  sidebarCards,
  sidebarFooter,
  userMenu,
  mainRef,
  children,
}: AppLayoutProps) {
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
          {/* The cards scroll with the navigation: they never squeeze it out on short screens (1.4.10). */}
          <SidebarContent className="gap-group px-2">
            {sidebarCards && sidebarCards.length > 0 && <SidebarCards cards={sidebarCards} />}
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
