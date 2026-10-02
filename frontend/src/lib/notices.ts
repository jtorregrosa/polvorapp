import { useCallback, useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router';

export interface Notice {
  /** A new id per notice, so each one is shown (and focused) afresh. */
  id: number;
  severity: 'success' | 'error';
  text: string;
}

export type Announce = (severity: Notice['severity'], text: string) => void;

/** The state a page hands over when it navigates here, e.g. "Comparsa created." or "Comparsa deleted.". */
export function noticeState(
  text: string,
  severity: Notice['severity'] = 'success',
): { notice: string; noticeSeverity: Notice['severity'] } {
  return { notice: text, noticeSeverity: severity };
}

/**
 * The page's outcome notice: starts with one handed over by the previous page (shown once, not
 * again after a reload or on coming back) and is replaced by each `announce`; `clear` removes it
 * when a later action makes it out of date. Rendered as an `AlertBanner` with `focusOnMount`, so
 * focus never gets lost when a control goes away.
 */
export function useNotice(): [Notice | undefined, Announce, () => void] {
  const location = useLocation();
  const navigate = useNavigate();
  const [notice, setNotice] = useState<Notice | undefined>(() => {
    const state = location.state as { notice?: unknown; noticeSeverity?: unknown } | null;
    const handed = state?.notice;
    const severity = state?.noticeSeverity === 'error' ? 'error' : 'success';
    return typeof handed === 'string' ? { id: 0, severity, text: handed } : undefined;
  });

  // Forget the handed notice without leaving the page: the query (e.g. a list's filters) stays.
  useEffect(() => {
    if (location.state !== null) {
      void navigate(
        { pathname: location.pathname, search: location.search, hash: location.hash },
        { replace: true, state: null },
      );
    }
  }, [location.state, location.pathname, location.search, location.hash, navigate]);

  // Stable, so pages can list it as a dependency without recomputing on every render.
  const announce: Announce = useCallback((severity, text) => {
    setNotice((previous) => ({ id: (previous?.id ?? 0) + 1, severity, text }));
  }, []);
  const clear = useCallback(() => {
    setNotice(undefined);
  }, []);
  return [notice, announce, clear];
}
