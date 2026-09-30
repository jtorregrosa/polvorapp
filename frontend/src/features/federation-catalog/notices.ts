import { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router';

export interface Notice {
  /** A new id per notice, so each one is shown (and focused) afresh. */
  id: number;
  severity: 'success' | 'error';
  text: string;
}

export type Announce = (severity: Notice['severity'], text: string) => void;

/** The state a page hands over when it navigates here, e.g. "Comparsa created." or "Comparsa deleted.". */
export function noticeState(text: string): { notice: string } {
  return { notice: text };
}

/**
 * The page's outcome notice: starts with one handed over by the previous page (shown once, not
 * again after a reload or on coming back) and is replaced by each `announce`. Rendered as an
 * `AlertBanner` with `focusOnMount`, so focus never gets lost when a control goes away.
 */
export function useNotice(): [Notice | undefined, Announce] {
  const location = useLocation();
  const navigate = useNavigate();
  const [notice, setNotice] = useState<Notice | undefined>(() => {
    const handed = (location.state as { notice?: unknown } | null)?.notice;
    return typeof handed === 'string' ? { id: 0, severity: 'success', text: handed } : undefined;
  });

  useEffect(() => {
    if (location.state !== null) {
      void navigate('.', { replace: true, state: null });
    }
  }, [location.state, navigate]);

  const announce: Announce = (severity, text) => {
    setNotice((previous) => ({ id: (previous?.id ?? 0) + 1, severity, text }));
  };
  return [notice, announce];
}
