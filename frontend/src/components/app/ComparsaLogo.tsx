import { Flag } from 'lucide-react';
import { useState } from 'react';
import { cn } from '@/lib/cn';

export interface ComparsaLogoProps {
  /** The logo's URL (with its version), or null when the comparsa has none. */
  src: string | null;
  /** 32 px in rows, 40 px in the sidebar, 64 px in a record header. */
  size: 'sm' | 'md' | 'lg';
  /**
   * Describes the logo. Empty by default: the comparsa name is shown next to it, so repeating it
   * would only add noise for screen readers (spec: Logo display).
   */
  alt?: string;
  className?: string;
}

const SIZE = { sm: 'size-8 p-0.5', md: 'size-10 p-1', lg: 'size-16 p-1.5' } as const;
const ICON = { sm: 'size-4', md: 'size-5', lg: 'size-7' } as const;

/**
 * A comparsa's logo, or a neutral placeholder, on a light tile that keeps any logo visible in both
 * themes and on the night sidebar (spec: Logo display; add-comparsa-logos D8). The logo keeps its
 * shape inside the tile. A logo that cannot be loaded shows the placeholder, never a broken image.
 */
export function ComparsaLogo({ src, size, alt = '', className }: ComparsaLogoProps) {
  // The URL that failed; a new URL (a replaced logo) is tried again.
  const [failedSrc, setFailedSrc] = useState<string | null>(null);
  const showImage = src !== null && src !== failedSrc;
  // A logo that stands alone keeps its name when only the placeholder can be shown.
  const namedPlaceholder = !showImage && alt !== '';

  return (
    <span
      data-slot="comparsa-logo"
      data-size={size}
      role={namedPlaceholder ? 'img' : undefined}
      aria-label={namedPlaceholder ? alt : undefined}
      className={cn(
        'inline-flex shrink-0 items-center justify-center overflow-hidden rounded-md border border-border bg-logo-tile text-logo-tile-foreground',
        SIZE[size],
        className,
      )}
    >
      {showImage ? (
        <img
          src={src}
          alt={alt}
          className="size-full object-contain"
          onError={() => {
            setFailedSrc(src);
          }}
        />
      ) : (
        <Flag aria-hidden="true" className={ICON[size]} />
      )}
    </span>
  );
}
