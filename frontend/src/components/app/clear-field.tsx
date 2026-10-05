import { X } from 'lucide-react';
import { Button } from './Button';

/**
 * Room the field keeps on its right for {@link ClearFieldButton}, set or not, so showing the button
 * never moves anything around it (e.g. the filters of a list).
 */
export const CLEARABLE_FIELD_CLASS = 'pr-10';

/**
 * The "clear" cross inside the right end of a date or time field, after the browser's own picker
 * icon. Icon-only, so `label` is its accessible name; at least 32 px for touch (WCAG 2.5.8).
 */
export function ClearFieldButton({ label, onClear }: { label: string; onClear: () => void }) {
  return (
    <Button
      type="button"
      variant="quiet"
      size="sm"
      icon={X}
      aria-label={label}
      title={label}
      className="absolute top-1/2 right-1 size-8 -translate-y-1/2 px-0 text-muted-foreground hover:text-foreground has-[>svg]:px-0 pointer-coarse:h-8"
      onClick={onClear}
    />
  );
}
