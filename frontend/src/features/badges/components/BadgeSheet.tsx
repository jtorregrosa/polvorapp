import { IdCard } from 'lucide-react';
import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getDownloadBadgeSheetUrl } from '@/api/generated/badges/badges';
import { useGetFederation } from '@/api/generated/federation/federation';
import type { ArquebusierRowResponse, FederationResponse } from '@/api/generated/model';
import { apiDownloadPost } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { DetailSheet } from '@/components/app/DetailSheet';
import { RadioCards } from '@/components/app/RadioCards';
import { saveFile } from '@/lib/download';
import { BADGE_LANGUAGES, type BadgeBatch, type BadgeLanguage } from '../batch';
import { badgeFailure, type BadgeFailure } from '../problems';

export interface BadgeSheetProps {
  batch: BadgeBatch;
  /** The batch's rows the screen has loaded: for the incomplete counts and to name arquebusiers. */
  rows: readonly ArquebusierRowResponse[];
  /**
   * Called, once the panel closes, with the selected ids the registry no longer has, so the screen
   * drops them; until then the panel and its explanation stay in place.
   */
  onUnknownIds: (ids: string[]) => void;
  /** The trigger cannot be used, e.g. beyond 200 selected; the element with `disabledReasonId` says why. */
  disabled?: boolean;
  disabledReasonId?: string;
  /** Completes the trigger's accessible name, e.g. the comparsa. */
  context?: string;
}

const languageOf = (language: string): BadgeLanguage =>
  language.startsWith('ca') ? 'ca-ES-valencia' : language.startsWith('en') ? 'en' : 'es-ES';

/** No issued license: none, or pending (spec: Incomplete badges are warnings). */
const withoutLicense = (row: ArquebusierRowResponse) =>
  row.licenseStatus === null || row.licenseStatus === 'PENDING';

/**
 * Spec "Badge screens": the batch, the labels' language (the user's by default), what will print
 * incomplete, the reminder to print at 100 %, and the download. A refused download says why; a
 * photo that cannot be read names the arquebusiers, and arquebusiers no longer in the registry are
 * handed back to the screen.
 */
export function BadgeSheet({
  batch,
  rows,
  onUnknownIds,
  disabled,
  disabledReasonId,
  context,
}: BadgeSheetProps) {
  const { t, i18n } = useTranslation('badges');
  const languageLabel = useId();
  const languageHint = useId();
  // Each failure mounts a new banner, so it is announced and focused again.
  const [attempt, setAttempt] = useState(0);
  const [language, setLanguage] = useState<BadgeLanguage>(() => languageOf(i18n.language));
  const [pending, setPending] = useState(false);
  const [saved, setSaved] = useState<string>();
  const [failure, setFailure] = useState<BadgeFailure>();
  const settings = useGetFederation();
  const logoMissing = (settings.data?.data as FederationResponse | undefined)?.logo === null;

  const noPhoto = rows.filter((row) => !row.hasIdPhoto).length;
  const noLicense = rows.filter(withoutLicense).length;
  const summary =
    batch.kind === 'comparsa'
      ? t('sheet.batchComparsa', { count: rows.length, comparsa: batch.comparsaName })
      : t('sheet.batchSelection', { count: batch.arquebusierIds.length });

  const download = async () => {
    if (pending) return;
    setPending(true);
    setSaved(undefined);
    setFailure(undefined);
    const body =
      batch.kind === 'comparsa'
        ? { comparsaId: batch.comparsaId, language }
        : { arquebusierIds: [...batch.arquebusierIds], language };
    try {
      const file = await apiDownloadPost(getDownloadBadgeSheetUrl(), body);
      const name = file.fileName ?? 'polvorapp-badges.pdf';
      saveFile(file.blob, name);
      setSaved(t('sheet.saved', { name }));
    } catch (error: unknown) {
      setFailure(badgeFailure(error, batch.kind === 'selection' ? batch.arquebusierIds : []));
      setAttempt((value) => value + 1);
    } finally {
      setPending(false);
    }
  };

  // Closing hands back the unknown ids and forgets the last outcome, so a reopened panel starts clean.
  const failureText = (reason: BadgeFailure): string => {
    if (reason.kind === 'unknownIds') return t('selection.removed', { count: reason.ids.length });
    if (reason.kind === 'reason') return t(`errors.${reason.key}`);
    const names = namesOf(rows, reason.ids);
    // A name the screen has not loaded is counted instead, never left as a blank.
    return names.length === reason.ids.length && names.length > 0
      ? t('errors.photoUnreadable', { names: names.join('; ') })
      : t('errors.photoUnreadableCount', { count: reason.ids.length });
  };

  const onOpenChange = (open: boolean) => {
    if (open) return;
    if (failure?.kind === 'unknownIds') onUnknownIds(failure.ids);
    setFailure(undefined);
    setSaved(undefined);
  };

  return (
    <DetailSheet
      title={t('sheet.title')}
      description={t('sheet.description')}
      trigger={{
        label: t('action'),
        icon: IdCard,
        context: context && t('actionContext', { comparsa: context }),
        disabled,
        disabledReasonId,
      }}
      onOpenChange={onOpenChange}
    >
      <div className="flex flex-col gap-5">
        <p>{summary}</p>
        <div className="flex flex-col gap-2">
          <p id={languageLabel} className="font-medium">
            {t('sheet.language')}
          </p>
          <RadioCards
            aria-labelledby={languageLabel}
            aria-describedby={languageHint}
            name="badge-language"
            value={language}
            onChange={(value) => {
              setLanguage(languageOf(value));
            }}
            options={BADGE_LANGUAGES.map((code) => ({ value: code, label: t(`sheet.languages.${code}`) }))}
          />
          <p id={languageHint} className="text-help text-muted-foreground">
            {t('sheet.languageHint')}
          </p>
        </div>
        {(noPhoto > 0 || noLicense > 0 || logoMissing) && (
          <AlertBanner severity="warning" title={t('sheet.incompleteTitle')} live={false}>
            <ul className="list-disc ps-5">
              {noPhoto > 0 && <li>{t('sheet.noPhoto', { count: noPhoto })}</li>}
              {noLicense > 0 && <li>{t('sheet.noLicense', { count: noLicense })}</li>}
              {logoMissing && <li>{t('sheet.noLogo')}</li>}
            </ul>
          </AlertBanner>
        )}
        <p className="text-help text-muted-foreground">{t('sheet.printNote')}</p>
        <div>
          <Button
            type="button"
            pending={pending}
            onClick={() => {
              void download();
            }}
          >
            {t('sheet.download')}
          </Button>
        </div>
        <p role="status" className="text-help text-muted-foreground empty:hidden">
          {saved}
        </p>
        {failure && (
          <AlertBanner key={attempt} severity="error" title={t('sheet.failedTitle')} focusOnMount>
            {failureText(failure)}
          </AlertBanner>
        )}
      </div>
    </DetailSheet>
  );
}

/** "Surname, Name" of each id that is loaded, in the order given. */
function namesOf(rows: readonly ArquebusierRowResponse[], ids: readonly string[]): string[] {
  const byId = new Map(rows.map((row) => [row.id, row]));
  return ids
    .map((id) => byId.get(id))
    .filter((row): row is ArquebusierRowResponse => row !== undefined)
    .map((row) => `${row.lastName}, ${row.firstName}`);
}
