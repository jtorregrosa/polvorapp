import { cn } from 'cn';
import { Eye, EyeOff } from 'lucide-react';
import { useState, type ComponentProps } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

export type PasswordInputProps = Omit<ComponentProps<typeof Input>, 'type'> & {
  /** `current-password` to sign in, `new-password` when choosing one (password managers rely on it). */
  autoComplete: 'current-password' | 'new-password';
};

/**
 * A password field with a show/hide toggle, so people can check what they typed on a phone.
 * Use inside {@link FormField}.
 */
export function PasswordInput({ autoComplete, className, ...props }: PasswordInputProps) {
  const { t } = useTranslation('ui');
  const [visible, setVisible] = useState(false);

  return (
    <div className="relative">
      <Input
        type={visible ? 'text' : 'password'}
        autoComplete={autoComplete}
        autoCapitalize="none"
        spellCheck={false}
        className={cn('pr-10', className)}
        {...props}
      />
      <Button
        type="button"
        variant="ghost"
        size="icon-sm"
        className="absolute inset-y-0.5 right-0.5"
        aria-label={t('password.show')}
        aria-pressed={visible}
        onClick={() => {
          setVisible((current) => !current);
        }}
      >
        {visible ? <EyeOff aria-hidden="true" /> : <Eye aria-hidden="true" />}
      </Button>
    </div>
  );
}
