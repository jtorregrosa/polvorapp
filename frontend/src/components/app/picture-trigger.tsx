import { ImageUp, Pencil, Trash2 } from 'lucide-react';
import { useState, type ReactNode, type Ref } from 'react';
import { useTranslation } from 'react-i18next';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { cn } from '@/lib/cn';

interface PictureTriggerProps {
  ref: Ref<HTMLButtonElement>;
  /** The picture, or the placeholder whose visible text names the button. */
  children: ReactNode;
  /** The frame's own look, e.g. the checkerboard behind a transparent logo. */
  frameClassName?: string;
  /**
   * With a picture: the button's name, e.g. "Logo of Comparsa Norte, options", and the button
   * opens a menu. Without one, the button opens the file chooser and its visible text names it.
   */
  menuName?: string;
  canRemove: boolean;
  disabled: boolean;
  /** Opening a chosen image: the button keeps focus and its name, marked busy, and ignores input. */
  busy?: boolean;
  describedBy?: string;
  onReplace: () => void;
  onRemove: () => void;
  /** Focus moved from the button to another element (not lost because the button went away). */
  onFocusMovedAway?: () => void;
}

/**
 * The picture as its own button (spec: Picture actions; refine-navigation-and-lists D7), used by
 * `PhotoUpload variant="picture"`: a menu to replace or remove it, or the file chooser when there
 * is none. An overlay hints at it on hover and keyboard focus; the button's name says it anyway.
 * Radix returns focus to the button when the menu closes.
 */
export function PictureTrigger({
  ref,
  children,
  frameClassName,
  menuName,
  canRemove,
  disabled,
  busy = false,
  describedBy,
  onReplace,
  onRemove,
  onFocusMovedAway,
}: PictureTriggerProps) {
  const { t } = useTranslation('ui');
  const [menuOpen, setMenuOpen] = useState(false);
  const button = (
    <button
      ref={ref}
      type="button"
      disabled={disabled}
      aria-label={menuName}
      aria-describedby={describedBy}
      aria-busy={busy || undefined}
      onClick={menuName === undefined && !busy ? onReplace : undefined}
      onBlur={(event) => {
        if (event.relatedTarget !== null) onFocusMovedAway?.();
      }}
      // At least 44 × 44 px (spec: Picture actions); the frame matches the section variant's.
      className={cn(
        'group relative flex min-h-11 w-40 items-center justify-center overflow-hidden rounded-md border border-input focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-50',
        frameClassName ?? 'bg-muted',
      )}
    >
      {children}
      {/* A hint only, never information (the button's name says it all), so not "additional
          content" under WCAG 1.4.13. Always shown on touch screens, which have no hover. */}
      <span
        data-picture-overlay=""
        aria-hidden="true"
        className="pointer-events-none absolute top-2 right-2 flex size-8 items-center justify-center rounded-full border bg-card text-foreground opacity-0 shadow-e1 transition-opacity duration-100 ease-out group-hover:opacity-100 group-focus-visible:opacity-100 group-disabled:hidden pointer-coarse:opacity-100"
      >
        <Pencil className="size-4" />
      </span>
    </button>
  );

  if (menuName === undefined) return button;

  return (
    // Busy, the menu stays closed while the button keeps focus (no native `disabled`, WCAG 2.4.3).
    <DropdownMenu
      open={menuOpen && !busy}
      onOpenChange={(next) => {
        if (!busy || !next) setMenuOpen(next);
      }}
    >
      {/* Radix opens on pointer down by its own `disabled`, not the button's. */}
      <DropdownMenuTrigger asChild disabled={disabled}>
        {button}
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" className="min-w-44">
        <DropdownMenuItem onSelect={onReplace}>
          <ImageUp aria-hidden="true" />
          {t('pictureActions.replace')}
        </DropdownMenuItem>
        {canRemove && (
          <DropdownMenuItem variant="destructive" onSelect={onRemove}>
            <Trash2 aria-hidden="true" />
            {t('pictureActions.remove')}
          </DropdownMenuItem>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
