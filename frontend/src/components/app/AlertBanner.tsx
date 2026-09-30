import { cva } from 'class-variance-authority';
import { cn } from 'cn';
import { CircleCheck, CircleX, Info, TriangleAlert, type LucideIcon } from 'lucide-react';
import { useEffect, useRef, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

export type AlertSeverity = 'info' | 'success' | 'warning' | 'error';

const banner = cva('flex gap-3 rounded-lg px-4 py-3 text-sm', {
  variants: {
    severity: {
      info: 'bg-info-soft text-info-soft-foreground',
      success: 'bg-success-soft text-success-soft-foreground',
      warning: 'bg-warning-soft text-warning-soft-foreground',
      error: 'bg-destructive-soft text-destructive-soft-foreground',
    } satisfies Record<AlertSeverity, string>,
  },
});

const ICONS: Record<AlertSeverity, LucideIcon> = {
  info: Info,
  success: CircleCheck,
  warning: TriangleAlert,
  error: CircleX,
};

export interface AlertBannerProps {
  severity: AlertSeverity;
  title?: string;
  children: ReactNode;
  className?: string;
  /**
   * Moves focus to the message when shown: for the outcome of an action whose control went away
   * (e.g. a confirmed deactivation), so it is read and focus is not lost (WCAG 2.4.3).
   */
  focusOnMount?: boolean;
}

/**
 * Inline message for a page or form. Errors interrupt assistive technology (`role="alert"`);
 * other severities are polite (`role="status"`). Status regions are only announced reliably when
 * their content changes after they are rendered, so render them where the message will appear.
 * The severity is always spoken, not only coloured.
 */
export function AlertBanner({
  severity,
  title,
  children,
  className,
  focusOnMount = false,
}: AlertBannerProps) {
  const { t } = useTranslation('ui');
  const region = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (focusOnMount) {
      region.current?.focus();
    }
  }, [focusOnMount]);
  const Icon = ICONS[severity];
  const severityLabel = <span className="sr-only">{`${t(`alert.${severity}`)}: `}</span>;

  return (
    <div
      ref={region}
      role={severity === 'error' ? 'alert' : 'status'}
      data-severity={severity}
      tabIndex={focusOnMount ? -1 : undefined}
      className={cn(banner({ severity }), 'outline-none', className)}
    >
      <Icon aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
      <div className="flex flex-col gap-0.5">
        {title ? (
          <p className="font-medium">
            {severityLabel}
            {title}
          </p>
        ) : (
          severityLabel
        )}
        <div>{children}</div>
      </div>
    </div>
  );
}
