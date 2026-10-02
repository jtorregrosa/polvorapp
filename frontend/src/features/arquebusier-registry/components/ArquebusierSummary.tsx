import { useTranslation } from 'react-i18next';
import type { ArquebusierResponse } from '@/api/generated/model';
import { KeyFacts, type KeyFact } from '@/components/app/KeyFacts';
import { todayIso } from '@/lib/dates';
import { useFormatters } from '@/lib/format';

const DATE: Intl.DateTimeFormatOptions = { day: '2-digit', month: '2-digit', year: 'numeric' };

/** How much of a license's validity has passed, 0 to 1; undefined without both dates. */
function elapsed(issuedOn: string | null, expiresOn: string | null): number | undefined {
  if (!issuedOn || !expiresOn) return undefined;
  const start = Date.parse(issuedOn);
  const total = Date.parse(expiresOn) - start;
  if (!(total > 0)) return undefined;
  return Math.min(1, Math.max(0, (Date.parse(todayIso()) - start) / total));
}

function useKeyFacts(arquebusier: ArquebusierResponse): KeyFact[] {
  const { t } = useTranslation('registry');
  const { date } = useFormatters();
  const formatDate = (iso: string) => date(new Date(`${iso}T12:00:00Z`), DATE);
  const license = arquebusier.license;
  const passed = elapsed(license?.issuedOn ?? null, license?.expiresOn ?? null);
  return [
    {
      id: 'license',
      label: t('detail.keyFacts.licenseExpires'),
      value: !license
        ? t('detail.keyFacts.noLicense')
        : license.expiresOn
          ? formatDate(license.expiresOn)
          : t('detail.keyFacts.pending'),
      meter:
        passed === undefined
          ? undefined
          : {
              value: passed,
              label: t('detail.keyFacts.licenseElapsed', { percent: Math.round(passed * 100) }),
            },
    },
    {
      id: 'federationId',
      label: t('form.federationId'),
      value: <span className="font-mono text-id">{arquebusier.federationId}</span>,
    },
    {
      id: 'nationalId',
      label: t('form.nationalId'),
      value: <span className="font-mono text-id">{arquebusier.nationalId}</span>,
    },
    {
      id: 'course',
      label: t('detail.keyFacts.course'),
      value: arquebusier.trainingCompletedOn
        ? formatDate(arquebusier.trainingCompletedOn)
        : t('detail.keyFacts.noCourse'),
    },
    { id: 'weapons', label: t('detail.keyFacts.weapons'), value: String(arquebusier.ownedWeapons.length) },
  ];
}

/** The facts under the header (spec: Registry screens). */
export function RecordFacts({ arquebusier }: { arquebusier: ArquebusierResponse }) {
  const { t } = useTranslation('registry');
  const items = useKeyFacts(arquebusier);
  return <KeyFacts label={t('detail.keyFacts.label')} items={items} />;
}
