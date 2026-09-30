import { Check, Copy, Printer } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';

type CopyOutcome = 'none' | 'copied' | 'failed';

export interface RecoveryCodeListProps {
  codes: readonly string[];
}

/**
 * Single-use recovery codes, shown once: monospace, easy to copy or print. Copying announces the
 * result to screen readers, including a failure, since the codes cannot be shown again.
 */
export function RecoveryCodeList({ codes }: RecoveryCodeListProps) {
  const { t } = useTranslation('ui');
  const [outcome, setOutcome] = useState<CopyOutcome>('none');

  const copy = async (): Promise<void> => {
    setOutcome('none'); // Cleared first, so a repeated result is announced again.
    try {
      await navigator.clipboard.writeText(codes.join('\n'));
      setOutcome('copied');
    } catch {
      // Clipboard blocked (permissions, insecure context): the codes stay visible to copy by hand.
      setOutcome('failed');
    }
  };

  return (
    <div className="grid gap-3">
      {/* eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`. */}
      <ul
        role="list"
        aria-label={t('recoveryCodes.label')}
        className="grid grid-cols-2 gap-2 rounded-md border bg-muted/50 p-4 font-mono text-sm"
      >
        {codes.map((code) => (
          <li key={code} className="tracking-wider">
            {code}
          </li>
        ))}
      </ul>
      <div className="flex flex-wrap gap-2 print:hidden">
        <Button type="button" variant="outline" onClick={() => void copy()}>
          {outcome === 'copied' ? <Check aria-hidden="true" /> : <Copy aria-hidden="true" />}
          {t('recoveryCodes.copy')}
        </Button>
        <Button
          type="button"
          variant="outline"
          onClick={() => {
            window.print();
          }}
        >
          <Printer aria-hidden="true" />
          {t('recoveryCodes.print')}
        </Button>
      </div>
      <p role="status" className="min-h-5 text-sm text-muted-foreground">
        {outcome === 'none' ? '' : t(`recoveryCodes.${outcome}`)}
      </p>
    </div>
  );
}
