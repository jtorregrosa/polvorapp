import { useId } from 'react';
import { cn } from '@/lib/cn';

/**
 * The PolvorApp mark (ADR-0015): the two-tongue flame with the ember gradient, centred in a square
 * box so the sidebar rail keeps it in place. Decorative; the product name is always next to it or
 * in the accessible name of its link. Same artwork as docs/design/brand/svg/symbol. The gradient
 * stops are fixed brand colours, not themed tokens: the flame looks the same on every surface.
 */
export function PolvorAppMark({ className }: { className?: string }) {
  // One gradient per instance: a shared id would break when the first mark leaves the page.
  const gradient = `polvorapp-flame-${useId()}`;
  const fill = `url(#${gradient})`;

  return (
    <svg
      viewBox="0 0 32 32"
      aria-hidden="true"
      focusable="false"
      className={cn('size-8 shrink-0', className)}
    >
      <defs>
        <linearGradient id={gradient} gradientUnits="userSpaceOnUse" x1="0" y1="32" x2="0" y2="0">
          <stop offset="0" stopColor="#ff9a52" />
          <stop offset="1" stopColor="#d9480f" />
        </linearGradient>
      </defs>
      <path
        fill={fill}
        d="M8.29 24.74C7.36 14.81 13.58 16.49 16.77 8.81C16.82 13.11 12.60 16.40 11.29 18.62C9.87 21.03 9.88 26.46 12.03 28.96C11.04 24.30 16.14 21.59 18.80 18.57C24.89 11.67 21.95 5.86 16.33 0.00C16.76 4.88 10.54 10.16 7.76 13.60C4.66 17.44 6.95 22.53 8.29 24.74Z"
      />
      <path
        fill={fill}
        d="M23.67 11.93C22.78 23.54 10.31 22.51 14.29 32.00C15.82 28.45 19.57 26.48 22.05 23.61C25.20 19.97 26.18 16.37 23.67 11.93Z"
      />
    </svg>
  );
}
