import { Download, Smartphone } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { getCapturePackage } from '@/api/generated/distribution/distribution';
import type { DistributionDayResponse } from '@/api/generated/model';
import { responseData } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { useFormatters } from '@/lib/format';
import { readPackage, savePackage, type StoredPackage } from '../offline/store';
import { captureRoute } from '../capture-route';
import { problemMessage } from '../problems';

/** Asks the browser to keep the device's data under storage pressure; false when it will not (or cannot). */
async function persistStorage(): Promise<boolean> {
  // Missing in some WebViews and in tests, although the DOM types promise it.
  const storage = (navigator as Partial<Navigator>).storage;
  if (typeof storage?.persist !== 'function') return false;
  try {
    return await storage.persist();
  } catch {
    return false;
  }
}

const EXPIRY: Intl.DateTimeFormatOptions = {
  day: 'numeric',
  month: 'long',
  hour: '2-digit',
  minute: '2-digit',
};

interface DayCaptureProps {
  day: DistributionDayResponse;
  /** The signed-in Admin, who owns what the device keeps. */
  userId: string;
}

/**
 * The powder day's handovers for Admins of the edition in progress (spec: Handover screens): how many
 * holders have collected, "Prepare for offline capture" (downloads the day into the device, SEC-14)
 * and "Open capture" once the device holds it.
 */
export function DayCapture({ day, userId }: DayCaptureProps) {
  const { t } = useTranslation('distribution');
  const { date } = useFormatters();
  const [stored, setStored] = useState<StoredPackage>();
  const [persistent, setPersistent] = useState<boolean>();
  const [preparing, setPreparing] = useState(false);
  const [failure, setFailure] = useState<string>();

  useEffect(() => {
    let current = true;
    readPackage(day.id, userId).then(
      (found) => {
        if (current) setStored(found);
      },
      // A device without IndexedDB holds nothing: preparing it says why it cannot.
      () => undefined,
    );
    return () => {
      current = false;
    };
  }, [day.id, userId]);

  const prepare = async () => {
    setPreparing(true);
    setFailure(undefined);
    try {
      const pkg = responseData(await getCapturePackage(day.id));
      setStored(await savePackage(userId, pkg, new Date()));
      setPersistent(await persistStorage());
    } catch (error) {
      setFailure(problemMessage(t, error));
    } finally {
      setPreparing(false);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <h3 className="text-label text-foreground">{t('capture.section')}</h3>
      {day.handovers && (
        <p className="text-body text-foreground">
          {t('capture.delivered', { recorded: day.handovers.recorded, holders: day.handovers.holders })}
        </p>
      )}
      {failure && <AlertBanner severity="error">{failure}</AlertBanner>}
      {stored && (
        <p className="text-help text-muted-foreground" role="status">
          {t('capture.prepared', { date: date(new Date(stored.expiresAt), EXPIRY) })}
          {persistent === false && <> {t('capture.notPersistent')}</>}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button variant="secondary" icon={Download} pending={preparing} onClick={() => void prepare()}>
          {t('capture.prepare')}
        </Button>
        {stored && (
          <Button asChild variant="primary" icon={Smartphone}>
            <Link to={captureRoute(day.id)}>{t('capture.open')}</Link>
          </Button>
        )}
      </div>
    </div>
  );
}
