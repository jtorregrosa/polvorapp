import { cva } from 'class-variance-authority';
import { cn } from '@/lib/cn';
import type { ParseKeys } from 'i18next';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { statusStyle, UNKNOWN_STATUS, type StatusKind, type StatusTone } from './status';

const badge = cva(
  'inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-xs font-medium whitespace-nowrap',
  {
    variants: {
      tone: {
        success: 'bg-success-soft text-success-soft-foreground',
        warning: 'bg-warning-soft text-warning-soft-foreground',
        destructive: 'bg-destructive-soft text-destructive-soft-foreground',
        info: 'bg-info-soft text-info-soft-foreground',
        muted: 'bg-muted text-muted-foreground',
      } satisfies Record<StatusTone, string>,
    },
  },
);

export interface StatusBadgeProps {
  kind: StatusKind;
  /** Glossary code term, e.g. `EXPIRED`, `SUBMITTED`. */
  value: string;
  className?: string;
}

/**
 * Renders a domain status with a translated label, an icon and a semantic tone — never colour
 * alone (spec: Status semantics, WCAG 1.4.1).
 */
export function StatusBadge({ kind, value, className }: StatusBadgeProps) {
  const { t } = useTranslation('ui');
  const style = statusStyle(kind, value);

  useEffect(() => {
    if (!style && import.meta.env.DEV) {
      console.warn(`StatusBadge: no mapping for ${kind} "${value}"`);
    }
  }, [style, kind, value]);

  const { tone, icon: Icon } = style ?? UNKNOWN_STATUS;
  // Mapped values always have a label (checked by StatusBadge.test.tsx for all three locales).
  const label = style ? t(`status.${kind}.${value}` as ParseKeys<'ui'>) : value;

  return (
    <span data-status-badge="" data-tone={tone} className={cn(badge({ tone }), className)}>
      <Icon aria-hidden="true" className="size-3.5 shrink-0" />
      {label}
    </span>
  );
}
