import { RefreshCw, Wifi, WifiOff, X } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { ConfirmFailure } from '@/components/app/confirm-failure';
import { SectionCard } from '@/components/app/SectionCard';
import { useFormatters } from '@/lib/format';
import type { StoredPackage } from '../offline/store';
import type { HandoverSyncState } from '../offline/useHandoverSync';
import { problemMessage } from '../problems';
import { ApiProblemError } from '@/api/http';

const WHEN: Intl.DateTimeFormatOptions = {
  day: 'numeric',
  month: 'long',
  hour: '2-digit',
  minute: '2-digit',
};

interface CaptureStatusProps {
  online: boolean;
  pending: number;
  conflicts: number;
  sync: HandoverSyncState;
  /** Syncing needs a session: off while it has ended. */
  canSync: boolean;
  stored: StoredPackage;
  /** Clears the day's package once nothing is left to sync; resolves to false when something is. */
  onClose: () => Promise<boolean>;
}

/**
 * The capture screen's status (spec: Handover screens): connectivity, what is left to sync and in
 * conflict, the last sync and "Sync now", when the list was downloaded and when it will be cleared
 * (SEC-14), and closing the capture. Each state is said in words, not by colour alone.
 */
export function CaptureStatus({
  online,
  pending,
  conflicts,
  sync,
  canSync,
  stored,
  onClose,
}: CaptureStatusProps) {
  const { t } = useTranslation('distribution');
  const { date } = useFormatters();
  const last = sync.last;
  const failure =
    last?.status === 'failed'
      ? last.code
        ? t('capture.status.failed', {
            reason: problemMessage(t, new ApiProblemError(409, { code: last.code })),
          })
        : t('capture.status.failedUnknown')
      : undefined;

  return (
    <SectionCard title={t('capture.status.label')}>
      {/* eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`. */}
      <ul role="list" className="flex flex-col gap-1 text-body">
        <li className="flex items-center gap-2 font-semibold">
          {online ? (
            <Wifi aria-hidden="true" className="size-4" />
          ) : (
            <WifiOff aria-hidden="true" className="size-4" />
          )}
          {/* The state that matters most outdoors: said when it changes (WCAG 4.1.3). */}
          <span role="status">{online ? t('capture.status.online') : t('capture.status.offline')}</span>
        </li>
        <li>{t('capture.status.pending', { count: pending })}</li>
        {conflicts > 0 && <li>{t('capture.status.conflicts', { count: conflicts })}</li>}
        <li className="text-muted-foreground">
          <span>
            {sync.running
              ? t('capture.status.syncing')
              : sync.lastSyncedAt
                ? t('capture.status.lastSync', { time: date(sync.lastSyncedAt, WHEN) })
                : t('capture.status.neverSynced')}
          </span>
        </li>
      </ul>
      {/* A sync that leaves conflicts is said; each later sync is not. */}
      <p role="status" className="sr-only">
        {conflicts > 0 ? t('capture.status.conflicts', { count: conflicts }) : ''}
      </p>
      {last?.status === 'busy' && <AlertBanner severity="info">{t('capture.status.busy')}</AlertBanner>}
      {failure && <AlertBanner severity="warning">{failure}</AlertBanner>}
      <p className="text-help text-muted-foreground">
        {t('capture.status.kept', {
          downloaded: date(new Date(stored.downloadedAt), WHEN),
          expires: date(new Date(stored.expiresAt), WHEN),
        })}
      </p>
      <div className="flex flex-wrap gap-2">
        <Button
          icon={RefreshCw}
          pending={sync.running}
          disabled={!online || !canSync || pending === 0}
          onClick={() => void sync.sync()}
        >
          {t('capture.status.syncNow')}
        </Button>
        <ConfirmDialog
          tone="primary"
          title={t('capture.status.closeTitle')}
          description={t('capture.status.closeDescription')}
          confirmLabel={t('capture.status.closeConfirm')}
          onConfirm={async () => {
            if (!(await onClose())) throw new ConfirmFailure(t('capture.status.closeBlocked'));
          }}
          trigger={
            <Button variant="secondary" icon={X}>
              {t('capture.status.close')}
            </Button>
          }
        />
      </div>
    </SectionCard>
  );
}
