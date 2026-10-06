import * as React from 'react';

const MOBILE_BREAKPOINT = 768;

/** Below this width the sidebar is a drawer, so tablets keep the content's full width (`lg`). */
const SIDEBAR_DRAWER_BREAKPOINT = 1024;

export function useIsMobile() {
  return useIsBelow(MOBILE_BREAKPOINT);
}

/** Local edit (UI audit T1): whether the sidebar is a drawer (below 1024 px). */
export function useIsSidebarDrawer() {
  return useIsBelow(SIDEBAR_DRAWER_BREAKPOINT);
}

function useIsBelow(breakpoint: number) {
  // Local edit (redesign-design-system): read the width at once, so a phone never first paints
  // the desktop layout (e.g. a table before its stacked list).
  const [isMobile, setIsMobile] = React.useState<boolean | undefined>(
    () => typeof window !== 'undefined' && window.innerWidth < breakpoint,
  );

  React.useEffect(() => {
    const mql = window.matchMedia(`(max-width: ${String(breakpoint - 1)}px)`);
    const onChange = () => {
      setIsMobile(window.innerWidth < breakpoint);
    };
    mql.addEventListener('change', onChange);
    setIsMobile(window.innerWidth < breakpoint);
    return () => mql.removeEventListener('change', onChange);
  }, [breakpoint]);

  return !!isMobile;
}
