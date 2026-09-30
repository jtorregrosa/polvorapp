import { useTranslation } from 'react-i18next';
import { useGetSystemInfo } from '@/api/generated/system/system';

/** API version from the system information endpoint; a translated notice when unavailable. */
export function VersionFooter() {
  const { t } = useTranslation();
  const systemInfo = useGetSystemInfo({ query: { staleTime: Infinity } });

  // The paragraph is always rendered so the footer keeps its height while loading.
  let text = '';
  if (systemInfo.isSuccess) {
    text = t('shell.footer.version', { version: systemInfo.data.data.version });
  } else if (systemInfo.isError) {
    text = t('shell.footer.versionUnavailable');
  }

  return <p className="shell-version">{text}</p>;
}
