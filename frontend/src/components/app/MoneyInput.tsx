import { cn } from '@/lib/cn';
import { useId, type ComponentProps } from 'react';
import { useTranslation } from 'react-i18next';
import { Input } from '@/components/ui/input';

export type MoneyInputProps = Omit<ComponentProps<typeof Input>, 'type' | 'inputMode'>;

/**
 * An amount in euros (design D10): a text field with the decimal keypad and a `€` suffix, read to
 * screen readers as "in euros" with the field. Its value is the typed text, so it is never
 * reformatted while typing; read it with `parseMoney`, which accepts a comma or a point and refuses
 * more than two decimals. Use inside {@link FormField} with `width="short"`.
 */
export function MoneyInput({ className, 'aria-describedby': describedBy, ...props }: MoneyInputProps) {
  const { t } = useTranslation('ui');
  const currencyId = useId();

  return (
    <div className="relative">
      <Input
        type="text"
        inputMode="decimal"
        autoComplete="off"
        spellCheck={false}
        className={cn('pr-8 tabular-nums', className)}
        aria-describedby={[describedBy, currencyId].filter(Boolean).join(' ')}
        {...props}
      />
      <span
        aria-hidden="true"
        className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-muted-foreground"
      >
        €
      </span>
      {/* Read only as the field's description, so "in euros" is heard once (`hidden` stays referable). */}
      <span id={currencyId} hidden>
        {t('moneyInput.currency')}
      </span>
    </div>
  );
}
