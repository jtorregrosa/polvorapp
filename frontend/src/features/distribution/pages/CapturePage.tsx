import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, LogIn } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation, useNavigate, useParams } from 'react-router';
import type { AccountResponse } from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { EmptyState } from '@/components/app/EmptyState';
import { PageHeader } from '@/components/app/PageHeader';
import { ForbiddenPage } from '@/features/identity-access/pages/ForbiddenPage';
import { fetchSession, SESSION_QUERY_KEY, signInPath } from '@/features/identity-access/session';
import { useEditionDates } from '@/features/festival-editions/dates';
import { captureHolders, panelModeOf, type CaptureHolder, type PanelMode } from '../capture/holders';
import { CaptureStatus } from '../capture/CaptureStatus';
import { Conflicts } from '../capture/Conflicts';
import { HandoverPanel } from '../capture/HandoverPanel';
import { HolderList } from '../capture/HolderList';
import { closeCapture, listQueue, readDevicePackage, type CapturedHandover } from '../offline/store';
import { useCaptureHousekeeping } from '../offline/useCaptureHousekeeping';
import { useHandoverSync, useOnline } from '../offline/useHandoverSync';

/** How long the session check may take before the screen stops waiting for it (e.g. a captive portal). */
const SESSION_CHECK_TIMEOUT_MS = 8_000;

/** How often a session that could not be checked is checked again. */
const SESSION_RECHECK_MS = 15_000;

/**
 * The session as the capture screen sees it: unlike the signed-in shell, an unreachable server is
 * not an error here, the screen keeps working from the device (add-offline-distribution-capture D4).
 */
type CaptureSession =
  | { status: 'checking' }
  | { status: 'signedIn'; account: AccountResponse }
  | { status: 'signedOut' }
  | { status: 'unreachable' };

function useCaptureSession(online: boolean): CaptureSession {
  const query = useQuery({
    queryKey: SESSION_QUERY_KEY,
    queryFn: () => fetchSession(AbortSignal.timeout(SESSION_CHECK_TIMEOUT_MS)),
    staleTime: 60_000,
    retry: false,
    // Without a network the request fails at once: the screen is told, instead of waiting.
    networkMode: 'always',
    // A session that could not be checked is checked again while the device is online.
    refetchInterval: (current) => (current.state.status === 'error' ? SESSION_RECHECK_MS : false),
  });
  // Connectivity is back: check at once rather than at the next interval.
  const { refetch, isError } = query;
  useEffect(() => {
    if (online && isError) void refetch();
  }, [online, isError, refetch]);
  if (query.data) return { status: 'signedIn', account: query.data };
  if (query.isPending) return { status: 'checking' };
  if (query.isError) return { status: 'unreachable' };
  return { status: 'signedOut' };
}

/**
 * What the device holds for the day: its package and the queue of the package's owner. Read once
 * for the day, whoever signs in meanwhile: the screen decides who may see it.
 */
function useDeviceDay(distributionId: string) {
  const query = useQuery({
    queryKey: ['capture-device', distributionId],
    queryFn: async () => {
      const stored = await readDevicePackage(distributionId);
      const queue = stored ? await listQueue(distributionId, stored.ownerUserId) : [];
      return { stored, queue };
    },
    // The device's store answers without a network.
    networkMode: 'always',
    retry: false,
  });
  const { refetch } = query;
  const reload = useCallback(async () => {
    await refetch();
  }, [refetch]);
  return { loaded: !query.isPending, failed: query.isError, data: query.data, reload };
}

const EMPTY_QUEUE: CapturedHandover[] = [];

/** The open panel: on which holder, as what, and a key so each opening starts afresh. */
interface Selection {
  entryId: string;
  mode: PanelMode;
  open: boolean;
  key: number;
}

/**
 * The powder day's handover capture (spec: Handover screens; UC-21): opens without connectivity from
 * the installed app and without a live session, reading the day from the device; Admins only when
 * the session can be checked. Records handovers on the device and syncs them only for a session
 * checked to be the package's owner (SEC-14).
 */
export function CapturePage() {
  const { distributionId = '' } = useParams();
  const { t } = useTranslation('distribution');
  const dates = useEditionDates();
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const online = useOnline();
  const session = useCaptureSession(online);
  const userId = session.status === 'signedIn' ? session.account.id : undefined;
  useCaptureHousekeeping(userId);
  const device = useDeviceDay(distributionId);
  // Signed in: only this user's package; otherwise the device's own (SEC-14).
  const stored =
    device.data?.stored && (!userId || device.data.stored.ownerUserId === userId)
      ? device.data.stored
      : undefined;
  const queue = stored ? (device.data?.queue ?? EMPTY_QUEUE) : EMPTY_QUEUE;
  const owner = stored?.ownerUserId;
  // Syncing and undoing need the owner's own session: never before it is checked, nor under another
  // user's cookie (SEC-14).
  const signedInOwner = session.status === 'signedIn' ? owner : undefined;
  const reload = device.reload;
  const sync = useHandoverSync(distributionId, signedInOwner, () => void reload());
  const [selection, setSelection] = useState<Selection>();
  const list = useRef<HTMLDivElement>(null);

  // The server ended the session during a sync: check it again, so the screen asks to sign in.
  const syncSignedOut = sync.last?.status === 'signedOut';
  useEffect(() => {
    if (syncSignedOut) void queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY });
  }, [syncSignedOut, queryClient]);

  const holders = useMemo<CaptureHolder[]>(
    () => (stored ? captureHolders(stored.package.rows, stored.package.handovers, queue) : []),
    [stored, queue],
  );
  const selected = selection && holders.find((h) => h.row.entryId === selection.entryId);

  const openHolder = (holder: CaptureHolder) => {
    setSelection((current) => ({
      entryId: holder.row.entryId,
      mode: panelModeOf(holder),
      open: true,
      key: (current?.key ?? 0) + 1,
    }));
  };
  // The holder's row, or the search when a search hides it (WCAG 2.4.3).
  const holderRow = (entryId: string): HTMLElement | null =>
    list.current?.querySelector<HTMLElement>(`[data-entry-id="${entryId}"]`) ??
    list.current?.querySelector<HTMLElement>('input') ??
    null;

  if (session.status === 'signedIn' && session.account.role !== 'ADMIN') {
    return <ForbiddenPage />;
  }

  if (!device.loaded) {
    return (
      <p role="status" className="text-muted-foreground">
        {t('capture.page.loading')}
      </p>
    );
  }

  if (!stored || !owner) {
    return (
      <>
        <PageHeader title={t('capture.page.title')} />
        {device.failed ? (
          <AlertBanner severity="error">{t('capture.page.storageUnavailable')}</AlertBanner>
        ) : (
          <EmptyState
            title={t('capture.page.noPackage')}
            description={t('capture.page.noPackageHint')}
            action={
              <Button asChild variant="secondary" icon={ArrowLeft}>
                <Link to="/distribution">{t('capture.page.toDistribution')}</Link>
              </Button>
            }
          />
        )}
      </>
    );
  }

  const { package: day } = stored;
  const pending = queue.filter((h) => h.state === 'pending').length;
  const conflicts = holders.filter((h) => h.state === 'CONFLICT');

  return (
    <>
      <PageHeader
        title={t('capture.page.title')}
        description={t('capture.page.description', { date: dates.day(day.date), location: day.location })}
      />
      {session.status === 'signedOut' && (
        // Announced: it may appear while capturing, when a sync finds the session ended.
        <AlertBanner severity="warning">
          <p>{t('capture.page.signInToSync')}</p>
          <Button asChild variant="secondary" size="sm" icon={LogIn}>
            <Link to={signInPath(location.pathname, location.search, 'expired')}>
              {t('capture.page.signIn')}
            </Link>
          </Button>
        </AlertBanner>
      )}
      <CaptureStatus
        online={online}
        pending={pending}
        conflicts={conflicts.length}
        sync={sync}
        canSync={signedInOwner !== undefined}
        stored={stored}
        onClose={async () => {
          const closed = await closeCapture(distributionId, owner);
          if (closed) void navigate(`/editions/${day.editionId}/distribution`);
          return closed;
        }}
      />
      <Conflicts
        conflicts={conflicts}
        ownerUserId={owner}
        onEdit={openHolder}
        onChanged={() => void reload()}
        focusHolder={(holder) => holderRow(holder.row.entryId)?.focus()}
      />
      <div ref={list} className="contents">
        <HolderList holders={holders} onOpen={openHolder} />
      </div>
      {selection && (
        <HandoverPanel
          key={selection.key}
          holder={selected}
          mode={selection.mode}
          open={selection.open}
          holders={holders}
          distributionId={distributionId}
          ownerUserId={owner}
          canUndo={online && signedInOwner !== undefined}
          onClose={() => {
            setSelection((current) => current && { ...current, open: false });
          }}
          onChanged={() => {
            void reload().then(() => sync.sync());
          }}
          fallbackFocus={() => holderRow(selection.entryId)}
        />
      )}
    </>
  );
}
