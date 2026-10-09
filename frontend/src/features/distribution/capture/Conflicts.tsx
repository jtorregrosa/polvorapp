import { Pencil, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { SectionCard } from '@/components/app/SectionCard';
import { useSaveNotice } from '@/components/app/save-notice';
import { removeCaptured } from '../offline/store';
import { UNKNOWN_REFUSAL } from '../offline/sync';
import { problemMessage } from '../problems';
import { ApiProblemError } from '@/api/http';
import { holderName, type CaptureHolder } from './holders';

interface ConflictsProps {
  conflicts: readonly CaptureHolder[];
  ownerUserId: string;
  /** Opens the handover's panel to correct it; saving sends it again. */
  onEdit: (holder: CaptureHolder) => void;
  onChanged: () => void;
  /** Focuses the holder's row: the discarded conflict, or the whole section, is gone (WCAG 2.4.3). */
  focusHolder: (holder: CaptureHolder) => void;
}

/**
 * The handovers the server refused (spec: Handover screens): each with its translated reason and,
 * when another device recorded the holder first, that handover; "Edit and send again" or "Discard".
 */
export function Conflicts({ conflicts, ownerUserId, onEdit, onChanged, focusHolder }: ConflictsProps) {
  const { t } = useTranslation('distribution');
  const notify = useSaveNotice();
  // Kept after the dialog closes, so its title stays while it fades and the row can take focus.
  const [discarding, setDiscarding] = useState<CaptureHolder>();
  const [confirming, setConfirming] = useState(false);
  if (conflicts.length === 0) return null;

  return (
    <SectionCard
      title={t('capture.conflicts.title')}
      description={t('capture.conflicts.description')}
      span="full"
    >
      {/* eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`. */}
      <ul role="list" className="flex flex-col gap-3">
        {conflicts.map((holder) => {
          const { row, captured } = holder;
          if (!captured) return null;
          const name = holderName(row);
          const code = captured.code ?? UNKNOWN_REFUSAL;
          return (
            <li key={captured.id} className="flex flex-col gap-2 rounded-md border bg-card p-3">
              <p className="font-semibold text-foreground">
                {t('capture.list.holder', { number: row.number, name })}
              </p>
              <p>{problemMessage(t, new ApiProblemError(409, { code }))}</p>
              {captured.existing && (
                <p className="text-help text-muted-foreground">
                  {t('capture.conflicts.existing', {
                    details: captured.existing.rentalFlaskNumber
                      ? t('capture.conflicts.existingFlask', { flask: captured.existing.rentalFlaskNumber })
                      : t('capture.conflicts.existingNoFlask'),
                  })}
                </p>
              )}
              <div className="flex flex-wrap gap-2">
                <Button
                  variant="secondary"
                  size="sm"
                  icon={Pencil}
                  onClick={() => {
                    onEdit(holder);
                  }}
                >
                  {t('capture.conflicts.edit')} <span className="sr-only">{name}</span>
                </Button>
                <Button
                  variant="quietDestructive"
                  size="sm"
                  icon={Trash2}
                  onClick={() => {
                    setDiscarding(holder);
                    setConfirming(true);
                  }}
                >
                  {t('capture.conflicts.discard')} <span className="sr-only">{name}</span>
                </Button>
              </div>
            </li>
          );
        })}
      </ul>
      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title={t('capture.conflicts.discardTitle', { name: discarding ? holderName(discarding.row) : '' })}
        description={t('capture.conflicts.discardDescription')}
        confirmLabel={t('capture.conflicts.discard')}
        onConfirm={async () => {
          if (discarding?.captured) await removeCaptured(discarding.captured.id, ownerUserId);
        }}
        onConfirmed={() => {
          onChanged();
          if (discarding) focusHolder(discarding);
          notify(t('capture.conflicts.discarded'));
        }}
      />
    </SectionCard>
  );
}
