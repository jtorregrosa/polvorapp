import { zodResolver } from '@hookform/resolvers/zod';
import { Trash2, Undo2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { undoHandover } from '@/api/generated/distribution/distribution';
import type { CaptureRowResponse } from '@/api/generated/model';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { explainFailure } from '@/components/app/confirm-failure';
import { DescriptionList } from '@/components/app/DescriptionList';
import { DetailSheet } from '@/components/app/DetailSheet';
import { EditSheet, type EditResult } from '@/components/app/EditSheet';
import { FormField } from '@/components/app/FormField';
import { RadioCards } from '@/components/app/RadioCards';
import { useSaveNotice } from '@/components/app/save-notice';
import { TextInput } from '@/components/app/TextInput';
import { useAppForm } from '@/components/app/use-app-form';
import { useFormatters } from '@/lib/format';
import { putCaptured, removeCaptured, removeServerHandover } from '../offline/store';
import { problemCode, problemMessage } from '../problems';
import { flaskTakenBy, holderName, rentsFlask, type CaptureHolder, type PanelMode } from './holders';

/** Limits of the API (spec: Powder handovers). */
const FLASK_MAX = 20;
const TRACEABILITY_MAX = 50;

interface Values {
  collectedBy: 'HOLDER' | 'PROXY';
  rentalFlaskNumber: string;
  traceability1: string;
  traceability2: string;
}

const EMPTY: Values = { collectedBy: 'HOLDER', rentalFlaskNumber: '', traceability1: '', traceability2: '' };

const blankToNull = (text: string): string | null => (text.trim() === '' ? null : text.trim());

interface HandoverPanelProps {
  /**
   * The holder the panel was opened on, as the device knows it now. Mount one panel per opening
   * (a `key`): what it opened on is kept, so it still shows while closing.
   */
  holder: CaptureHolder | undefined;
  mode: PanelMode;
  open: boolean;
  holders: readonly CaptureHolder[];
  distributionId: string;
  ownerUserId: string;
  /** Online with a session: a synced handover can be undone. */
  canUndo: boolean;
  onClose: () => void;
  /** Re-reads the device after a change. */
  onChanged: () => void;
  /** Where focus goes on close when what opened the panel is gone, e.g. a resolved conflict. */
  fallbackFocus: () => HTMLElement | null;
}

/**
 * A holder's handover (spec: Handover screens): recorded or edited on the device while it is not
 * synced, in a side panel (a bottom sheet on phones) checked like the server would (the collector,
 * the rented flask's number and its uniqueness, the lengths); a synced one is shown read-only and
 * may be undone online, after a confirmation naming the holder and the flask.
 */
export function HandoverPanel({ mode, ...props }: HandoverPanelProps) {
  return mode === 'synced' ? <SyncedPanel mode={mode} {...props} /> : <CapturePanel mode={mode} {...props} />;
}

function CapturePanel({
  holder,
  open,
  holders,
  distributionId,
  ownerUserId,
  onClose,
  onChanged,
  fallbackFocus,
}: HandoverPanelProps) {
  const { t } = useTranslation('distribution');
  const notify = useSaveNotice();
  const row = holder?.row;
  // The handover on the device when the panel opened: a sync may settle it while the Admin types.
  const [captured] = useState(holder?.captured);

  const schema = useMemo(
    () =>
      z
        .object({
          collectedBy: z.enum(['HOLDER', 'PROXY']),
          rentalFlaskNumber: z.string().max(FLASK_MAX, t('capture.panel.errors.tooLong', { max: FLASK_MAX })),
          traceability1: z
            .string()
            .max(TRACEABILITY_MAX, t('capture.panel.errors.tooLong', { max: TRACEABILITY_MAX })),
          traceability2: z
            .string()
            .max(TRACEABILITY_MAX, t('capture.panel.errors.tooLong', { max: TRACEABILITY_MAX })),
        })
        .superRefine((values, context) => {
          if (!row || !rentsFlask(row)) return;
          const flask = values.rentalFlaskNumber.trim();
          if (flask === '') {
            context.addIssue({
              code: 'custom',
              path: ['rentalFlaskNumber'],
              message: t('capture.panel.errors.flaskRequired'),
            });
            return;
          }
          const takenBy = flaskTakenBy(flask, row.entryId, holders);
          if (takenBy !== undefined) {
            context.addIssue({
              code: 'custom',
              path: ['rentalFlaskNumber'],
              message: t('capture.panel.errors.flaskTaken', { flask, number: takenBy }),
            });
          }
        }),
    [t, row, holders],
  );
  const form = useAppForm<Values>({ resolver: zodResolver(schema), defaultValues: EMPTY });

  const values = useMemo<Values>(
    () =>
      captured
        ? {
            collectedBy: captured.collectedBy === 'PROXY' ? 'PROXY' : 'HOLDER',
            rentalFlaskNumber: captured.rentalFlaskNumber ?? '',
            traceability1: captured.traceability1 ?? '',
            traceability2: captured.traceability2 ?? '',
          }
        : EMPTY,
    [captured],
  );

  const save = async (submitted: Values): Promise<EditResult> => {
    if (!row) return { status: 'kept' };
    const now = new Date().toISOString();
    const byProxy = submitted.collectedBy === 'PROXY' && row.proxy !== null;
    // A conflict is sent again as a new handover, in its place: its id may already be recorded
    // with other data.
    const resend = captured?.state === 'conflict';
    await putCaptured(
      {
        id: captured && !resend ? captured.id : crypto.randomUUID(),
        distributionId,
        ownerUserId,
        holderEntryId: row.entryId,
        distributionNumber: row.number,
        collectedBy: byProxy ? 'PROXY' : 'HOLDER',
        collectorEntryId: byProxy ? (row.proxy?.entryId ?? null) : null,
        rentalFlaskNumber: rentsFlask(row) ? blankToNull(submitted.rentalFlaskNumber) : null,
        traceability1: blankToNull(submitted.traceability1),
        traceability2: blankToNull(submitted.traceability2),
        collectedAt: captured?.collectedAt ?? now,
        capturedAt: captured && !resend ? captured.capturedAt : now,
        state: 'pending',
        code: null,
        existing: null,
      },
      resend ? captured.id : undefined,
    );
    onChanged();
    return { status: 'saved', notice: t('capture.panel.recorded') };
  };

  // Only on this device: confirmed first, as a discarded conflict is (WCAG 3.3.4).
  const remove = captured && row && (
    <ConfirmDialog
      title={t('capture.panel.removeTitle', { name: holderName(row) })}
      description={t('capture.panel.removeDescription')}
      confirmLabel={t('capture.panel.remove')}
      onConfirm={() => removeCaptured(captured.id, ownerUserId)}
      onConfirmed={() => {
        onClose();
        onChanged();
        notify(t('capture.panel.removed'));
      }}
      trigger={
        <Button type="button" variant="quietDestructive" size="sm" icon={Trash2}>
          {t('capture.panel.remove')}
        </Button>
      }
    />
  );

  return (
    <EditSheet
      title={row ? t('capture.panel.title', { name: holderName(row) }) : ''}
      description={row ? describe(t, row) : undefined}
      sectionName={t('capture.panel.sectionName')}
      form={form}
      values={values}
      open={open && row !== undefined}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      submitLabel={captured ? t('capture.panel.save') : t('capture.panel.record')}
      footerStart={remove}
      fallbackFocus={fallbackFocus}
      onSave={save}
    >
      {row?.proxy && (
        <FormField control={form.control} name="collectedBy" label={t('capture.panel.collector')}>
          {(field) => (
            <RadioCards
              {...field}
              options={[
                { value: 'HOLDER', label: t('capture.panel.byHolder') },
                {
                  value: 'PROXY',
                  label: t('capture.panel.byProxy', {
                    name: holderName(row.proxy ?? { lastName: '', firstName: '' }),
                    nationalId: row.proxy?.nationalId ?? '',
                  }),
                },
              ]}
            />
          )}
        </FormField>
      )}
      {row && rentsFlask(row) && (
        <FormField
          control={form.control}
          name="rentalFlaskNumber"
          label={t('capture.panel.flaskNumber')}
          width="short"
        >
          {(field) => <TextInput {...field} autoComplete="off" />}
        </FormField>
      )}
      <FormField
        control={form.control}
        name="traceability1"
        label={t('capture.panel.traceability1')}
        optional
        width="short"
      >
        {(field) => <TextInput {...field} autoComplete="off" />}
      </FormField>
      <FormField
        control={form.control}
        name="traceability2"
        label={t('capture.panel.traceability2')}
        optional
        width="short"
      >
        {(field) => <TextInput {...field} autoComplete="off" />}
      </FormField>
    </EditSheet>
  );
}

function SyncedPanel({
  holder,
  open,
  distributionId,
  ownerUserId,
  canUndo,
  onClose,
  onChanged,
  fallbackFocus,
}: HandoverPanelProps) {
  const { t } = useTranslation('distribution');
  const { date } = useFormatters();
  const notify = useSaveNotice();
  const [confirming, setConfirming] = useState(false);
  // The handover the panel opened on: still shown while the panel closes after an undo.
  const [recorded] = useState(holder?.recorded);
  if (!holder || !recorded) return null;
  const { row } = holder;
  const name = holderName(row);

  const undo = (
    <ConfirmDialog
      open={confirming}
      onOpenChange={setConfirming}
      title={t('capture.panel.undoTitle', { name })}
      description={
        recorded.rentalFlaskNumber
          ? t('capture.panel.undoDescription', { flask: recorded.rentalFlaskNumber })
          : t('capture.panel.undoDescriptionNoFlask')
      }
      confirmLabel={t('capture.panel.undo')}
      onConfirm={() =>
        explainFailure(
          async () => {
            try {
              await undoHandover(recorded.id, { version: recorded.version });
            } catch (error) {
              // Already undone elsewhere: what the Admin wanted.
              if (problemCode(error) !== 'distribution.notFound') throw error;
            }
            await removeServerHandover(distributionId, ownerUserId, recorded.id);
          },
          (error) => problemMessage(t, error),
        )
      }
      onConfirmed={() => {
        onClose();
        onChanged();
        notify(t('capture.panel.undone'));
      }}
    />
  );

  return (
    <>
      <DetailSheet
        title={t('capture.panel.title', { name })}
        description={describe(t, row)}
        open={open}
        onOpenChange={(next) => {
          if (!next) onClose();
        }}
        fallbackFocus={fallbackFocus}
        footerStart={
          canUndo ? (
            <Button
              variant="quietDestructive"
              size="sm"
              icon={Undo2}
              onClick={() => {
                setConfirming(true);
              }}
            >
              {t('capture.panel.undo')}
            </Button>
          ) : undefined
        }
      >
        <DescriptionList
          items={[
            {
              term: t('capture.panel.collector'),
              value:
                recorded.collectedBy === 'PROXY' && row.proxy
                  ? t('capture.panel.byProxy', {
                      name: holderName(row.proxy),
                      nationalId: row.proxy.nationalId ?? '',
                    })
                  : t('capture.panel.byHolder'),
            },
            ...(recorded.rentalFlaskNumber
              ? [{ term: t('capture.panel.flaskNumber'), value: recorded.rentalFlaskNumber }]
              : []),
            ...(recorded.traceability1
              ? [{ term: t('capture.panel.traceability1'), value: recorded.traceability1 }]
              : []),
            ...(recorded.traceability2
              ? [{ term: t('capture.panel.traceability2'), value: recorded.traceability2 }]
              : []),
          ]}
        />
        <p className="mt-4 text-help text-muted-foreground">
          {t('capture.panel.synced', { time: date(new Date(recorded.recordedAt), TIME) })}
        </p>
        {!canUndo && <p className="mt-2 text-help text-muted-foreground">{t('capture.panel.undoOffline')}</p>}
      </DetailSheet>
      {undo}
    </>
  );
}

const TIME: Intl.DateTimeFormatOptions = {
  day: 'numeric',
  month: 'long',
  hour: '2-digit',
  minute: '2-digit',
};

function describe(
  t: ReturnType<typeof useTranslation<'distribution'>>['t'],
  row: CaptureRowResponse,
): string {
  return t('capture.panel.description', {
    number: row.number,
    nationalId: row.nationalId ?? '',
    kg: row.powderKg,
    flask: t(`capture.list.flasks.${row.flask}`),
  });
}
