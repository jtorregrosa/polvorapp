import { useTranslation } from 'react-i18next';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';

/**
 * The "Trends" tab when its code cannot be loaded (offline, or replaced by a new deploy): it says so
 * and reloads the page to try again, since a failed lazy import is not retried by React.
 */
export default function TrendsUnavailable() {
  const { t } = useTranslation('insights');
  return (
    <LoadFailure
      error={null}
      consequence={t('trends.unavailable')}
      onRetry={() => {
        window.location.reload();
      }}
    />
  );
}
