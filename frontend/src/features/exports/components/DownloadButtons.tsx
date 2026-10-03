import { FileSpreadsheet, FileText } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiProblemError, apiDownload } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { saveFile } from '@/lib/download';

type Format = 'xlsx' | 'pdf';

interface DownloadButtonsProps {
  /** Already translated: what the buttons download, for their accessible names ("la lista de …"). */
  what: string;
  urls: Readonly<Record<Format, string>>;
}

/** The translated reason a download failed (spec: Exports screens). */
function reasonKey(error: unknown) {
  if (error instanceof ApiProblemError) {
    if (error.problem?.code === 'exports.notPrepared') return 'errors.notPrepared' as const;
    if (error.problem?.code === 'exports.auditUnavailable') return 'errors.auditUnavailable' as const;
    if (error.status === 429) return 'errors.tooMany' as const;
  }
  return 'errors.failed' as const;
}

/**
 * "Excel" and "PDF" downloads of one export (spec: Exports screens; design D9): the file is saved
 * with the name the server gives; the button pressed shows the download in progress, and a failure
 * says why.
 */
export function DownloadButtons({ what, urls }: DownloadButtonsProps) {
  const { t } = useTranslation('exports');
  const [pending, setPending] = useState<Format>();
  const [failure, setFailure] = useState<string>();
  const [saved, setSaved] = useState<string>();

  // One download at a time: a second click while one runs is ignored. A download started before the
  // user leaves the page still completes: the file was asked for.
  const download = async (format: Format) => {
    if (pending) return;
    setPending(format);
    setFailure(undefined);
    setSaved(undefined);
    try {
      const file = await apiDownload(urls[format]);
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
        <Button
          type="button"
          size="sm"
          variant="secondary"
          icon={FileSpreadsheet}
          pending={pending === 'xlsx'}
          aria-label={t('download.excelFor', { what })}
          onClick={() => {
            void download('xlsx');
          }}
        >
          {t('download.excel')}
        </Button>
        <Button
          type="button"
          size="sm"
          variant="secondary"
          icon={FileText}
          pending={pending === 'pdf'}
          aria-label={t('download.pdfFor', { what })}
          onClick={() => {
            void download('pdf');
          }}
        >
          {t('download.pdf')}
        </Button>
      </div>
      <p role="status" className="sr-only">
        {saved}
      </p>
      {failure && (
        <AlertBanner severity="error" title={t('download.failedTitle', { what })}>
          {failure}
        </AlertBanner>
      )}
    </div>
  );
}
