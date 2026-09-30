import { useEffect, useRef, type RefObject } from 'react';
import { useLocation } from 'react-router';

/**
 * Moves focus to the main content after client-side navigation (not on the first load), so
 * keyboard and screen-reader users start reading the new page (WCAG 2.4.3).
 */
export function useFocusMainOnNavigation(main: RefObject<HTMLElement | null>): void {
  const { pathname } = useLocation();
  const previousPathname = useRef(pathname);

  useEffect(() => {
    if (previousPathname.current === pathname) {
      return;
    }
    previousPathname.current = pathname;
    main.current?.focus();
  }, [pathname, main]);
}
