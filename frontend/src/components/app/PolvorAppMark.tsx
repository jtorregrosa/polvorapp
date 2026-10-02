import { cn } from '@/lib/cn';

/**
 * The PolvorApp mark (ADR-0013): a rounded "P" whose counter ends in an ember spark. Decorative;
 * the product name is always next to it or in the accessible name of its link.
 */
export function PolvorAppMark({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 32 32"
      aria-hidden="true"
      focusable="false"
      className={cn('size-8 shrink-0', className)}
    >
      <rect width="32" height="32" rx="8" className="fill-primary" />
      <path
        d="M11 24V8h6.5a5 5 0 0 1 0 10H11"
        fill="none"
        strokeWidth="3"
        strokeLinecap="round"
        strokeLinejoin="round"
        className="stroke-primary-foreground"
      />
      <circle cx="23" cy="9" r="2" className="fill-primary-foreground" />
    </svg>
  );
}
