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
  link: 'link',
} as const;

export interface ButtonProps extends Omit<ComponentProps<typeof ButtonPrimitive>, 'variant' | 'size'> {
  /** `primary` for the main action of a page or form, `secondary` for the others. */
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
  ...props
}: ButtonProps) {
  const { t } = useTranslation('ui');

  if (asChild) {
    return (
      <ButtonPrimitive
        variant={VARIANTS[variant]}
        size={size}
        asChild
        onClick={onClick}
        className={className}
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
      className={cn('aria-disabled:cursor-progress aria-disabled:opacity-50', className)}
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
