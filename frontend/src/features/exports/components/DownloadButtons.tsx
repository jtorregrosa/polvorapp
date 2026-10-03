import { FileSpreadsheet, FileText } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiProblemError, apiDownload } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { saveFile } from '@/lib/download';

type Format = 'xlsx' | 'pdf';

const ALL_FORMATS: readonly Format[] = ['xlsx', 'pdf'];

const FORMAT_BUTTON = {
  xlsx: { icon: FileSpreadsheet, text: 'download.excel', name: 'download.excelFor' },
  pdf: { icon: FileText, text: 'download.pdf', name: 'download.pdfFor' },
} as const;

interface DownloadButtonsProps {
  /** Already translated: what the buttons download, for their accessible names ("la lista de …"). */
  what: string;
  /** The URL of each format; a format without one is not offered. */
  urls: Readonly<Partial<Record<Format, string>>>;
  /** The formats offered, in order: both by default, `['pdf']` for a form. */
  formats?: readonly Format[];
  /**
   * Already translated, for a single download: its visible text instead of its format (e.g. "Print
   * form") and its accessible name, which must start with that text (WCAG 2.5.3), e.g. "Print form
   * for Ana". Ignored when several formats are offered.
   */
  label?: { text: string; name: string };
}

/** The translated reason a download failed (spec: Exports screens). */
function reasonKey(error: unknown) {
  if (error instanceof ApiProblemError) {
    switch (error.problem?.code) {
      case 'exports.notPrepared':
        return 'errors.notPrepared' as const;
      case 'exports.auditUnavailable':
      case 'distribution.auditUnavailable':
        return 'errors.auditUnavailable' as const;
      case 'proxies.notApplicable':
        return 'errors.notApplicable' as const;
      case 'proxies.licenseInvalid':
        return 'errors.licenseInvalid' as const;
      case 'storage.unavailable':
        return 'errors.storageUnavailable' as const;
      case 'proxies.notFound':
      case 'distribution.notFound':
        return 'errors.gone' as const;
    }
    if (error.status === 429) return 'errors.tooMany' as const;
  }
  return 'errors.failed' as const;
}

/**
 * "Excel" and "PDF" downloads of one export or document (spec: Exports screens, Distribution
 * screens): the file is saved with the name the server gives; the button pressed shows the download
 * in progress, and a failure says why.
 */
export function DownloadButtons({ what, urls, formats = ALL_FORMATS, label }: DownloadButtonsProps) {
  const { t } = useTranslation('exports');
  const [pending, setPending] = useState<Format>();
  const [failure, setFailure] = useState<string>();
  const [saved, setSaved] = useState<string>();
  const offered = formats.filter((format) => urls[format]);
  const single = offered.length === 1 ? label : undefined;

  // One download at a time: a second click while one runs is ignored. A download started before the
  // user leaves the page still completes: the file was asked for.
  const download = async (format: Format) => {
    const url = urls[format];
    if (pending || !url) return;
    setPending(format);
    setFailure(undefined);
    setSaved(undefined);
    try {
      const file = await apiDownload(url);
      const name = file.fileName ?? `polvorapp.${format}`;
      saveFile(file.blob, name);
      setSaved(t('download.saved', { name }));
    } catch (error: unknown) {
      setFailure(t(reasonKey(error)));
    } finally {
      setPending(undefined);
    }
  };

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap gap-2">
        {offered.map((format) => {
          const button = FORMAT_BUTTON[format];
          return (
            <Button
              key={format}
              type="button"
              size="sm"
              variant="secondary"
              icon={button.icon}
              pending={pending === format}
              aria-label={single ? single.name : t(button.name, { what })}
              onClick={() => {
                void download(format);
              }}
            >
              {single ? single.text : t(button.text)}
            </Button>
          );
        })}
      </div>
      <p role="status" className="sr-only">
        {/* A second download while one runs is ignored: say what is still on its way. */}
        {pending ? t('download.inProgress', { what }) : saved}
      </p>
      {failure && (
        <AlertBanner severity="error" title={t('download.failedTitle', { what })}>
          {failure}
        </AlertBanner>
      )}
    </div>
  );
}
