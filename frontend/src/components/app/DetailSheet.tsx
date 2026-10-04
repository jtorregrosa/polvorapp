import { Eye, type LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Button as ButtonPrimitive } from '@/components/ui/button';
import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from '@/components/ui/sheet';
import { useIsMobile } from '@/hooks/use-mobile';
import { Button } from './Button';

export interface DetailSheetProps {
  /** Already translated, e.g. "Entry details". */
  title: string;
  description?: string;
  /**
   * The trigger's visible label and icon, "View details" by default. `context` completes its name
   * for screen readers when a list has one per row, e.g. "View details" + "Order validated".
   */
  trigger?: {
    label?: string;
    icon?: LucideIcon;
    context?: string;
    /**
     * The panel cannot be opened now. The button stays focusable (`aria-disabled`) and points to
     * the element with this id, which says why, so keyboard and screen reader users find the reason.
     */
    disabledReasonId?: string;
    disabled?: boolean;
  };
  /** Called when the panel opens or closes, e.g. to reset what it showed. */
  onOpenChange?: (open: boolean) => void;
  /** The read-only content, usually a `DescriptionList`. Rendered only while the panel is open. */
  children: ReactNode;
}

/**
 * Shows a record's details without leaving the list (add-audit-privacy, design D12): a button opens
 * them read-only in a side panel, a bottom sheet on phones. Close, Escape or clicking outside close
 * it, and focus returns to the button. Use `EditSheet` when the panel changes something.
 */
export function DetailSheet({ title, description, trigger, onOpenChange, children }: DetailSheetProps) {
  const { t } = useTranslation('ui');
  const side = useIsMobile() ? 'bottom' : 'right';
  const Icon = trigger?.icon ?? Eye;

  return (
    <Sheet onOpenChange={onOpenChange}>
      <SheetTrigger asChild>
        <ButtonPrimitive
          variant="outline"
          size="sm"
          className="gap-1.5 aria-disabled:cursor-not-allowed aria-disabled:opacity-50"
          aria-disabled={trigger?.disabled === true ? true : undefined}
          aria-describedby={trigger?.disabled ? trigger.disabledReasonId : undefined}
          onClick={(event) => {
            // Radix skips opening when the click was prevented.
            if (trigger?.disabled) event.preventDefault();
          }}
        >
          <Icon aria-hidden="true" />
          {trigger?.label ?? t('detail.view')}
          {trigger?.context && (
            <>
              {' '}
              <span className="sr-only">{trigger.context}</span>
            </>
          )}
        </ButtonPrimitive>
      </SheetTrigger>
      <SheetContent
        side={side}
        data-side={side}
        // The footer's Close is the panel's only close button.
        showCloseButton={false}
        // Without a description, tell Radix there is none on purpose.
        {...(description ? {} : { 'aria-describedby': undefined })}
        className="w-full gap-0 sm:max-w-xl"
      >
        <SheetHeader className="border-b px-6 pt-6 pb-4">
          <SheetTitle className="font-display text-section">{title}</SheetTitle>
          {description && <SheetDescription className="text-help">{description}</SheetDescription>}
        </SheetHeader>
        <div
          role="region"
          aria-label={title}
          // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex -- the keyboard must reach a scrollable region without controls (WCAG 2.1.1), as in DataTable
          tabIndex={0}
          className="min-h-0 flex-1 overflow-y-auto px-6 py-4"
        >
          {children}
        </div>
        <div className="flex justify-end border-t px-6 py-4">
          <SheetClose asChild>
            <Button variant="secondary">{t('detail.close')}</Button>
          </SheetClose>
        </div>
      </SheetContent>
    </Sheet>
  );
}
