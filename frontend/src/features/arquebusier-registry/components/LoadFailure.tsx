import { RotateCw } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { problemMessage } from '../problems';

export interface LoadFailureProps {
  error: unknown;
  /** What could not be loaded and what that means here, e.g. that the shown data may be outdated. */
  consequence?: string;
  onRetry: () => unknown;
  className?: string;
}

/** A request for data that failed: says why and what it affects, and offers to try again. */
export function LoadFailure({ error, consequence, onRetry, className }: LoadFailureProps) {
  const { t } = useTranslation('registry');
  return (
    <AlertBanner severity="error" className={className}>
      <span className="flex flex-col items-start gap-2">
        <span>{consequence ? `${problemMessage(t, error)} ${consequence}` : problemMessage(t, error)}</span>
        <Button type="button" variant="secondary" size="sm" icon={RotateCw} onClick={() => void onRetry()}>
          {t('load.retry')}
        </Button>
      </span>
    </AlertBanner>
  );
}
