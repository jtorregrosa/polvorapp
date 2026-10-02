import { CircleCheck } from 'lucide-react';
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { SaveNoticeContext } from './save-notice';

/** How long a notice stays on screen. */
const VISIBLE_MS = 8000;
/** Pause between clearing and setting the text, so a repeated notice is announced again. */
const REANNOUNCE_MS = 50;

/**
 * Short confirmations of a completed action ("Changes saved"), mounted once by the shell (spec:
 * Detail pages in read mode): one polite status region, shown for a few seconds at the bottom of
 * the screen, that never takes the focus. Children call `useSaveNotice()` to get `notify`.
 */
export function SaveNoticeProvider({ children }: { children: ReactNode }) {
  const [text, setText] = useState('');
  const timers = useRef<number[]>([]);

  const clearTimers = () => {
    for (const timer of timers.current) window.clearTimeout(timer);
    timers.current = [];
  };

  const notify = useCallback((next: string) => {
    clearTimers();
    setText('');
    timers.current.push(
      window.setTimeout(() => {
        setText(next);
      }, REANNOUNCE_MS),
      window.setTimeout(() => {
        setText('');
      }, REANNOUNCE_MS + VISIBLE_MS),
    );
  }, []);

  useEffect(() => clearTimers, []);

  return (
    <SaveNoticeContext value={notify}>
      {children}
      {/* Always in the accessibility tree, so screen readers notice each new text. */}
      <p role="status" aria-live="polite" className="sr-only">
        {text}
      </p>
      {text && (
        <div
          aria-hidden="true"
          className="pointer-events-none fixed inset-x-0 bottom-0 z-50 flex justify-center p-gutter"
        >
          <p className="flex items-center gap-2 rounded-lg border border-transparent bg-foreground px-4 py-2.5 text-label text-background shadow-e2">
            <CircleCheck className="size-4 shrink-0" />
            {text}
          </p>
        </div>
      )}
    </SaveNoticeContext>
  );
}
