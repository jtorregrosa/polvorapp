import type { LucideIcon } from 'lucide-react';
import { useEffect, useId, useRef, useState, type ReactNode, type RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from 'react-router';
import { cn } from '@/lib/cn';
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupLabel,
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
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { useIsMobile } from '@/hooks/use-mobile';
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

/**
 * A section of the navigation (refine-navigation-and-lists D4): a labelled group for assistive
 * technology, or the unlabelled first section (Home).
 */
export interface NavigationSection {
  id: string;
  /** Already translated; none for the first section. */
  label?: string;
  items: readonly NavigationItem[];
}

/** A link card in the sidebar, under the mark: e.g. a FiringChief's comparsa with its logo. */
export interface SidebarCard {
  to: string;
  /** Already translated or proper name. */
  label: string;
  /** Shown before the label, e.g. a `ComparsaLogo`; decorative. */
  media: ReactNode;
}

export interface AppLayoutProps {
  /** The sections of the navigation, in order, each with at least one item. */
  navigation: readonly NavigationSection[];
  /** Cards under the mark, in this order (platform spec: Application shell); none when empty. */
  sidebarCards?: readonly SidebarCard[];
  /** Shown at the bottom of the sidebar (API version). */
  sidebarFooter?: ReactNode;
  /** End of the top bar: the signed-in user's menu, which holds the language and theme switchers. */
  userMenu?: ReactNode;
  /**
   * Up to five everyday destinations in a bar at the bottom of phones (below 768 px), e.g. a
   * FiringChief's; the drawer keeps the full navigation. None when empty.
   */
  bottomNavigation?: readonly NavigationItem[];
  mainRef?: RefObject<HTMLElement | null>;
  children: ReactNode;
}

/**
 * The motion of a text in the sidebar (refine-navigation-and-lists D3): it fades out at once when
 * the sidebar collapses, before the rail is reached, and in once the width is restored; it never
 * wraps while the width moves. Mark it `data-sidebar-label` (reduced motion drops the delay).
 */
const SIDEBAR_LABEL =
  'transition-opacity duration-100 ease-out delay-200 group-data-[collapsible=icon]:opacity-0 group-data-[collapsible=icon]:delay-0 group-data-[moving=true]/sidebar-wrapper:whitespace-nowrap';

/** Whether the sidebar is the icon rail: collapsed on a wide screen (phones keep the drawer). */
function useIconRail(): boolean {
  const { state, isMobile } = useSidebar();
  return state === 'collapsed' && !isMobile;
}

/** Closes the navigation drawer after a destination is chosen on a small screen. */
function useCloseDrawer(): () => void {
  const { isMobile, setOpenMobile } = useSidebar();
  return () => {
    if (isMobile) setOpenMobile(false);
  };
}

function NavigationMenu({ sections }: { sections: readonly NavigationSection[] }) {
  const { t } = useTranslation('ui');
  const { pathname } = useLocation();
  const currentTarget = currentNavigationTarget(
    pathname,
    sections.flatMap((section) => section.items),
  );

  return (
    <nav aria-label={t('nav.label')}>
      {sections.map((section) => (
        <NavigationGroup key={section.id} section={section} currentTarget={currentTarget} />
      ))}
    </nav>
  );
}

/**
 * One section: its label names a `role="group"`, so screen readers announce where an entry
 * belongs. Not a heading: three of them on every page would come before the page's `h1`.
 */
function NavigationGroup({
  section: { label, items },
  currentTarget,
}: {
  section: NavigationSection;
  currentTarget: string | undefined;
}) {
  const closeDrawer = useCloseDrawer();
  const headingId = useId();
  const rail = useIconRail();

  return (
    <SidebarGroup
      className="p-0"
      role={label ? 'group' : undefined}
      aria-labelledby={label ? headingId : undefined}
    >
      {/* In the rail the label gives way to a line in the same place, so nothing moves (D3). */}
      {label && (
        <SidebarGroupLabel
          asChild
          className="relative group-data-[collapsible=icon]:mt-0 group-data-[collapsible=icon]:opacity-100 after:pointer-events-none after:absolute after:inset-x-2 after:top-1/2 after:h-px after:bg-sidebar-border after:opacity-0 after:transition-opacity after:duration-100 group-data-[collapsible=icon]:after:opacity-100"
        >
          <div id={headingId}>
            <span data-sidebar-label="" className={SIDEBAR_LABEL}>
              {label}
            </span>
          </div>
        </SidebarGroupLabel>
      )}
      <SidebarMenu>
        {items.map((item) => {
          const { to, label: itemLabel, icon: Icon } = item;
          const current = to === currentTarget;
          const counted = item.count !== undefined && item.count > 0;
          const name =
            item.count !== undefined && item.count > 0 ? `${itemLabel}, ${item.countLabel}` : itemLabel;
          return (
            <SidebarMenuItem key={to}>
              <SidebarMenuButton
                asChild
                isActive={current}
                // In the rail the name is shown on hover and keyboard focus (D1); hidden otherwise.
                tooltip={name}
                // Current page: an ember bar, weight and icon, not background alone (WCAG 1.4.1).
                // 40 px and the same padding as in the rail, so the icons do not move (D1, D3).
                className="relative min-h-10 px-3 text-sidebar-muted-foreground before:absolute before:inset-y-1.5 before:left-0 before:w-1 before:rounded-full hover:text-sidebar-foreground data-[active=true]:font-semibold data-[active=true]:text-sidebar-foreground data-[active=true]:before:bg-sidebar-primary data-[active=true]:[&>svg]:text-sidebar-primary"
              >
                <Link
                  to={to}
                  aria-current={current ? 'page' : undefined}
                  // The name starts with the visible label (WCAG 2.5.3) and says what the badge counts;
                  // an aria-label is exact, where hidden text gets a space before the comma.
                  aria-label={counted ? name : undefined}
                  onClick={closeDrawer}
                >
                  <Icon aria-hidden="true" />
                  {/* Kept in the rail, clipped and faded: the link keeps its name (D1). */}
                  <span data-sidebar-label="" className={cn('break-words', SIDEBAR_LABEL)}>
                    {itemLabel}
                  </span>
                </Link>
              </SidebarMenuButton>
              {counted && !rail && <SidebarMenuBadge aria-hidden="true">{item.count}</SidebarMenuBadge>}
              {/* In the rail the counter is a dot; the count stays in the link's name and tooltip. */}
              {counted && rail && (
                <span
                  data-nav-dot=""
                  aria-hidden="true"
                  className="pointer-events-none absolute top-1.5 right-1.5 size-2 rounded-full bg-sidebar-primary"
                />
              )}
            </SidebarMenuItem>
          );
        })}
      </SidebarMenu>
    </SidebarGroup>
  );
}

/**
 * One sidebar card: in the icon rail only its logo shows, with the name in a tooltip (D1). The
 * tooltip is controlled and stays closed outside the rail, so the link gets no repeated description.
 */
function SidebarCardLink({
  to,
  label,
  media,
  current,
  rail,
  onChoose,
}: SidebarCard & { current: boolean; rail: boolean; onChoose: () => void }) {
  const [tooltipOpen, setTooltipOpen] = useState(false);
  return (
    <Tooltip open={rail && tooltipOpen} onOpenChange={setTooltipOpen}>
      <TooltipTrigger asChild>
        <Link
          to={to}
          aria-current={current ? 'page' : undefined}
          // In the rail only the logo shows; the name stays for assistive technology (D1).
          className="relative flex min-h-11 items-center gap-2.5 rounded-md bg-sidebar-accent py-1.5 pr-2 pl-3 text-label text-sidebar-foreground group-data-[collapsible=icon]:overflow-hidden group-data-[collapsible=icon]:bg-transparent group-data-[collapsible=icon]:p-0 before:absolute before:inset-y-2 before:left-0 before:w-1 before:rounded-full hover:bg-sidebar-border focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sidebar-ring aria-[current=page]:font-semibold aria-[current=page]:before:bg-sidebar-primary"
          onClick={onChoose}
        >
          {media}
          <span
            data-card-label=""
            data-sidebar-label=""
            className={cn('min-w-0 wrap-break-word', SIDEBAR_LABEL)}
          >
            {label}
          </span>
        </Link>
      </TooltipTrigger>
      <TooltipContent side="right" align="center">
        {label}
      </TooltipContent>
    </Tooltip>
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
  const rail = useIconRail();

  return (
    <nav aria-label={t('nav.comparsas')}>
      <ul className="flex flex-col gap-1">
        {cards.map(({ to, label, media }) => {
          const current = isCurrentPath(pathname, to);
          return (
            <li key={to}>
              <SidebarCardLink
                to={to}
                label={label}
                media={media}
                current={current}
                rail={rail}
                onChoose={closeDrawer}
              />
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
  const { t } = useTranslation('ui');
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
    // On wide screens the name says what the button does next (D9); the drawer keeps its state.
    <SidebarTrigger
      ref={trigger}
      aria-expanded={isMobile ? openMobile : undefined}
      aria-label={isMobile ? undefined : t(open ? 'nav.collapse' : 'nav.expand')}
      aria-keyshortcuts={isMobile ? undefined : 'Control+B Meta+B'}
      className="size-control"
    />
  );
}

/**
 * The phone's bottom bar (UI audit): each destination with its icon and label, the current one
 * marked by weight, colour and a bar (not colour alone). Hidden from 768 px and while a form shows
 * its own bottom action bar (globals.css).
 */
function BottomNavigation({ items }: { items: readonly NavigationItem[] }) {
  const { t } = useTranslation('ui');
  const { pathname } = useLocation();
  const currentTarget = currentNavigationTarget(pathname, items);
  return (
    <nav
      aria-label={t('nav.shortcuts')}
      data-slot="bottom-nav"
      className="fixed inset-x-0 bottom-0 z-30 border-t bg-card pb-safe shadow-e1 md:hidden"
    >
      {/* eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`. */}
      <ul role="list" className="flex min-h-bottom-bar items-stretch">
        {items.map(({ to, label, icon: Icon, count, countLabel }) => {
          const counted = count !== undefined && count > 0;
          return (
            <li key={to} className="flex min-w-0 flex-1">
              <Link
                to={to}
                aria-current={currentTarget === to ? 'page' : undefined}
                // The name starts with the visible label (WCAG 2.5.3) and says what the badge counts.
                aria-label={counted ? `${label}, ${countLabel}` : undefined}
                className="relative flex min-w-0 flex-1 flex-col items-center justify-center gap-0.5 px-1 py-1.5 text-center text-help break-words text-muted-foreground before:absolute before:inset-x-4 before:top-0 before:h-0.5 before:rounded-full focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring aria-[current=page]:font-semibold aria-[current=page]:text-foreground aria-[current=page]:before:bg-primary"
              >
                <span className="relative">
                  <Icon aria-hidden="true" className="size-5 shrink-0" />
                  {counted && (
                    <span
                      aria-hidden="true"
                      className="absolute -top-1.5 left-3.5 rounded-full bg-primary px-1 text-xs font-semibold text-primary-foreground tabular-nums"
                    >
                      {count}
                    </span>
                  )}
                </span>
                <span className="max-w-full">{label}</span>
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
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
  bottomNavigation = [],
  mainRef,
  children,
}: AppLayoutProps) {
  const { t } = useTranslation();
  const topBar = useRef<HTMLElement>(null);
  // Phones only: elsewhere the sidebar holds the same links, which must not be there twice.
  const showBottomBar = useIsMobile() && bottomNavigation.length > 0;

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
        <Sidebar collapsible="icon">
          <SidebarHeader>
            <Link
              to="/"
              // The same padding in the rail, so the mark does not move (D3).
              className="flex items-center gap-2.5 overflow-hidden rounded-md p-1 font-display text-section font-bold whitespace-nowrap text-sidebar-foreground focus-visible:outline-2 focus-visible:outline-sidebar-ring"
            >
              <PolvorAppMark />
              <span data-sidebar-label="" className={SIDEBAR_LABEL}>
                {t('app.name')}
              </span>
            </Link>
          </SidebarHeader>
          {/* The cards scroll with the navigation: they never squeeze it out on short screens (1.4.10). */}
          {/* Vertical room for the focus ring of the first and last entries (WCAG 2.4.7). */}
          <SidebarContent className="gap-group px-2 py-1">
            {sidebarCards && sidebarCards.length > 0 && <SidebarCards cards={sidebarCards} />}
            <NavigationMenu sections={navigation} />
          </SidebarContent>
          {sidebarFooter && (
            // Hidden in the rail, also from assistive technology (D1).
            <SidebarFooter
              data-sidebar-label=""
              className={cn(
                'px-4 text-xs text-sidebar-muted-foreground group-data-[collapsible=icon]:hidden',
                SIDEBAR_LABEL,
              )}
            >
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
            {/* Room for the bottom bar, so the end of the page is never under it. */}
            {showBottomBar && (
              <div
                data-slot="bottom-nav-spacer"
                aria-hidden="true"
                className="h-bottom-bar shrink-0 md:hidden"
              />
            )}
          </SidebarInset>
          {showBottomBar && <BottomNavigation items={bottomNavigation} />}
        </div>
      </SidebarProvider>
    </SaveNoticeProvider>
  );
}
