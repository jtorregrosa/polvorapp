import { useTranslation } from 'react-i18next';
import type { ComplianceWarning } from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { useFormatters } from '@/lib/format';

/** Dates in sentences are written out ("10 de marzo de 2030"): read the same by every screen reader. */
const DATE: Intl.DateTimeFormatOptions = { dateStyle: 'long' };

export interface ComplianceWarningsProps {
  /** As the server derives them (spec: Compliance warnings), in rule order; never derived here. */
  warnings: readonly ComplianceWarning[];
  /** The license expiry, for the expired and expiring sentences. */
  licenseExpiresOn: string | null;
  /** The age the server derived, for the under-age sentence. */
  age: number;
  /** Where each warning is resolved on the page, e.g. the license section: a link after its sentence. */
  targets?: Partial<Record<ComplianceWarning, { href: string; label: string }>>;
}

/**
 * The compliance warnings of one arquebusier in words, in one warning message that says they never
 * block saving (spec: Registry screens, BR-04), each linking to where it is resolved when given.
 * Renders nothing without warnings.
 */
export function ComplianceWarnings({ warnings, licenseExpiresOn, age, targets }: ComplianceWarningsProps) {
  const { t } = useTranslation('insights');
  const { date: formatDate } = useFormatters();
  if (warnings.length === 0) return null;

  const date = licenseExpiresOn ? formatDate(new Date(`${licenseExpiresOn}T12:00:00Z`), DATE) : '';
  return (
    // A lasting state shown with the page, not news: no live region (design guide, Accessibility rules).
    <AlertBanner severity="warning" title={t('warnings.title')} live={false}>
      <ul className="list-disc pl-5">
        {warnings.map((code) => {
          const target = targets?.[code];
          return (
            <li key={code}>
              {t(`warnings.${code}`, { date, age })}
              {target && (
                <>
                  {' '}
                  <a
                    href={target.href}
                    className="font-semibold underline underline-offset-4 hover:no-underline"
                  >
                    {target.label}
                  </a>
                </>
              )}
            </li>
          );
        })}
      </ul>
      <p className="mt-2">{t('warnings.hint')}</p>
    </AlertBanner>
  );
}
