import { ComplianceWarning, type LicenseStatus } from '@/api/generated/model';

/**
 * The license status to show: a valid license with the LICENSE_EXPIRING warning, as the server
 * derives it, is shown as "expiring soon" (design-system: Status semantics). Never derived here.
 */
export function licenseBadgeValue(
  status: LicenseStatus,
  warnings: readonly ComplianceWarning[],
): LicenseStatus | 'EXPIRING' {
  return status === 'VALID' && warnings.includes(ComplianceWarning.LICENSE_EXPIRING) ? 'EXPIRING' : status;
}
