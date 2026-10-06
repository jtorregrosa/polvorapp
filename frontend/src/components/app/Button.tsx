import { cn } from '@/lib/cn';
import { LoaderCircle, type LucideIcon } from 'lucide-react';
import type { ComponentProps } from 'react';
import { useTranslation } from 'react-i18next';
import { Button as ButtonPrimitive } from '@/components/ui/button';

const VARIANTS = {
  primary: 'default',
  secondary: 'outline',
  destructive: 'destructive',
  quiet: 'ghost',
  /** A destructive action that should not weigh like the others (e.g. "Remove" in a row); always confirmed. */
  quietDestructive: 'ghost',
  link: 'link',
} as const;

/** Classes a variant adds to its primitive's. */
const EXTRA: Partial<Record<keyof typeof VARIANTS, string>> = {
  quietDestructive: 'text-destructive hover:bg-destructive-soft hover:text-destructive-soft-foreground',
};

export interface ButtonProps extends Omit<ComponentProps<typeof ButtonPrimitive>, 'variant' | 'size'> {
  /**
   * `primary` for the main action of a page or form, `secondary` for the others; `quietDestructive`
   * for a destructive action beside them (spec: Action hierarchy).
   */
  variant?: keyof typeof VARIANTS;
  size?: 'default' | 'sm' | 'lg';
  icon?: LucideIcon;
  /**
   * While an action runs: ignores clicks and is marked busy and unavailable, with a spinner and
   * "one moment" for screen readers. It stays focusable, so focus is not lost mid-action.
   */
  pending?: boolean;
}

/**
 * The application's button (design guide: forms). Submitting buttons show a pending state and do
 * nothing while pending; use `asChild` to style a link as a button.
 */
export function Button({
  variant = 'primary',
  size = 'default',
  icon: Icon,
  pending = false,
  disabled,
  children,
  asChild,
  onClick,
  className,
  'aria-label': ariaLabel,
  ...props
}: ButtonProps) {
  const { t } = useTranslation('ui');
  // An aria-label replaces the content, so the pending words must join it to be heard.
  const label = pending && ariaLabel ? `${ariaLabel}, ${t('button.pending')}` : ariaLabel;

  if (asChild) {
    return (
      <ButtonPrimitive
        variant={VARIANTS[variant]}
        size={size}
        asChild
        onClick={onClick}
        className={cn(EXTRA[variant], className)}
        aria-label={ariaLabel}
        {...props}
      >
        {children}
      </ButtonPrimitive>
    );
  }

  return (
    <ButtonPrimitive
      variant={VARIANTS[variant]}
      size={size}
      disabled={disabled}
      aria-disabled={pending || undefined}
      aria-busy={pending || undefined}
      aria-label={label}
      className={cn('aria-disabled:cursor-progress aria-disabled:opacity-50', EXTRA[variant], className)}
      onClick={(event) => {
        if (pending) {
          event.preventDefault();
          return;
        }
        onClick?.(event);
      }}
      {...props}
    >
      {pending ? (
        <LoaderCircle aria-hidden="true" className="animate-spin" />
      ) : (
        Icon && <Icon aria-hidden="true" />
      )}
      {children}
      {pending && <span className="sr-only">{t('button.pending')}</span>}
    </ButtonPrimitive>
  );
}
