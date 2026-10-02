import { useEffect, useRef, type ReactNode } from 'react';

export interface ActionBarProps {
  /** The page's only primary action, e.g. the submit `Button`; it is shown last. */
  primary: ReactNode;
  /** Secondary actions (e.g. "Cancel"), shown before the primary one. */
  secondary?: ReactNode;
  /** A short status before the actions, e.g. "Unsaved changes"; announced politely. */
  status?: ReactNode;
}

/**
 * The form's actions, fixed at the bottom of the screen while the form scrolls (spec: Action
 * hierarchy): secondary actions, then the primary one at its natural width. The page keeps the
 * bar's real height as scroll padding (`globals.css`), even when long labels wrap it onto two rows,
 * so it never hides the focused field (SC 2.4.11). On very short screens (zoomed in, landscape
 * phones) the bar scrolls with the form instead, so it does not take the reading area.
 */
export function ActionBar({ primary, secondary, status }: ActionBarProps) {
  const bar = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const element = bar.current;
    if (!element) return;
    const root = document.documentElement;
    const observer = new ResizeObserver(() => {
      root.style.setProperty('--action-bar-height', `${String(element.offsetHeight)}px`);
    });
    observer.observe(element);
    return () => {
      observer.disconnect();
      root.style.removeProperty('--action-bar-height');
    };
  }, []);

  return (
    <div
      ref={bar}
      data-slot="action-bar"
      className="sticky bottom-0 z-10 -mx-1 flex min-h-action-bar flex-wrap items-center justify-end gap-2 border-t bg-background px-1 py-3"
    >
      <div role="status" className="mr-auto text-help text-muted-foreground empty:hidden">
        {status}
      </div>
      {secondary}
      {primary}
    </div>
  );
}
