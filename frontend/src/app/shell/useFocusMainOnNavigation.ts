import { useEffect, useRef, type RefObject } from 'react';
import { useLocation } from 'react-router';

/**
 * Moves focus to the main content after client-side navigation (not on the first load), so
 * keyboard and screen-reader users start reading the new page (WCAG 2.4.3). That includes arriving
 * in this shell from the other one (signing in or out), when the focused control disappeared.
 */
export function useFocusMainOnNavigation(main: RefObject<HTMLElement | null>): void {
  const { pathname, key } = useLocation();
  // The first location of the app has the key "default"; any later one came from a navigation.
  const previousPathname = useRef(key === 'default' ? pathname : undefined);

  useEffect(() => {
    if (previousPathname.current === pathname) {
      return;
    }
    previousPathname.current = pathname;
    // A page that already moved focus inside itself (e.g. to a notice) keeps it.
    if (main.current && !main.current.contains(document.activeElement)) {
      main.current.focus();
    }
  }, [pathname, main]);
}
