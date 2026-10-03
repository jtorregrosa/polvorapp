import { zodResolver } from '@hookform/resolvers/zod';
import { Clock } from 'lucide-react';
import { useMemo } from 'react';
import type { Path } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useSaveDistributionSlots } from '@/api/generated/distribution/distribution';
import type { DistributionDayResponse, SlotRequest } from '@/api/generated/model';
import { EditSheet, type EditResult } from '@/components/app/EditSheet';
import { FormField } from '@/components/app/FormField';
import { TimeInput } from '@/components/app/TimeInput';
import { useAppForm } from '@/components/app/use-app-form';
import { applyFieldErrors, isStale, problemMessage } from '../problems';
import { useDistributionRefresh } from '../queries';
import { slotsSchema, type SlotsInput, type SlotsValues } from '../schemas';

/** Every comparsa of the day, with or without a slot, by name: one row each in the panel. */
function slotRows(day: DistributionDayResponse, locale: string): SlotsValues['slots'] {
  return [
    ...day.slots.map((slot) => ({ ...slot })),
    ...(day.withoutSlot ?? []).map((comparsa) => ({
      comparsaId: comparsa.id,
      comparsaName: comparsa.name,
      startsAt: '',
    })),
  ].sort((a, b) => a.comparsaName.localeCompare(b.comparsaName, locale));
}

/**
 * The slots sent: the rows with a time, each with its row in the panel, so the API's reasons
 * (`slots[i]…`, by position in the request) land on the right comparsa.
 */
function requestOf(rows: SlotsInput['slots']) {
  const sent = rows.flatMap((row, index) => (row.startsAt ? [{ index, row }] : []));
  const slots: SlotRequest[] = sent.map(({ row }) => ({
    comparsaId: row.comparsaId,
    startsAt: row.startsAt,
  }));
  const fields = new Map<string, Path<SlotsValues>>(
    sent.flatMap(({ index }, position) => [
      [`slots[${position}].comparsaId`, `slots.${index}.startsAt`],
      [`slots[${position}].startsAt`, `slots.${index}.startsAt`],
    ]),
  );
  return { slots, fields };
}

interface SlotsSheetProps {
  editionId: string;
  day: DistributionDayResponse;
  /** Hides the button while the slots cannot be changed, keeping an open panel and its reason. */
  hideTrigger?: boolean;
}

/**
 * An Admin edits a day's slots in a side panel (spec: Distribution slots): one start time per
 * comparsa, empty for no slot, saved as one set with the day's version. On a conflict the panel
 * shows the slots as they are now, so a save never drops another person's change unseen.
 */
export function SlotsSheet({ editionId, day, hideTrigger }: SlotsSheetProps) {
  const { t, i18n } = useTranslation('distribution');
  const save = useSaveDistributionSlots();
  const refresh = useDistributionRefresh(editionId);
  const values = useMemo<SlotsValues>(() => ({ slots: slotRows(day, i18n.language) }), [day, i18n.language]);
  const form = useAppForm<SlotsValues, unknown, SlotsInput>({
    resolver: zodResolver(slotsSchema),
    defaultValues: values,
  });

  // The rows come from the form, so each label stays with its value when the panel is reset.
  const rows = form.watch('slots');

  const onSave = async (submitted: SlotsInput): Promise<EditResult> => {
    const { slots, fields } = requestOf(submitted.slots);
    try {
      await save.mutateAsync({ id: day.id, data: { version: day.version, slots } });
    } catch (error) {
      if (isStale(error)) {
        const plan = await refresh();
        const fresh = plan?.days.find((candidate) => candidate.id === day.id);
        if (fresh) form.reset({ slots: slotRows(fresh, i18n.language) });
        return { status: 'conflict', reason: problemMessage(t, error) };
      }
      return applyFieldErrors(error, fields, form.setError)
        ? { status: 'kept' }
        : { status: 'rejected', reason: problemMessage(t, error) };
    }
    await refresh();
    return { status: 'saved' };
  };

  return (
    <EditSheet
      title={t(`slots.editTitle.${day.type}`)}
      description={t('slots.hint')}
      sectionName={t(`day.name.${day.type}`)}
      trigger={{ label: t('slots.edit'), icon: Clock, context: t(`slots.editContext.${day.type}`) }}
      hideTrigger={hideTrigger}
      form={form}
      values={values}
      savedText={t('slots.saved')}
      onSave={onSave}
    >
      {rows.map((row, index) => (
        <FormField
          key={row.comparsaId}
          control={form.control}
          name={`slots.${index}.startsAt`}
          label={t('slots.timeFor', { comparsa: row.comparsaName })}
          width="short"
          optional
        >
          {(field) => <TimeInput {...field} clearable clearSubject={row.comparsaName} />}
        </FormField>
      ))}
    </EditSheet>
  );
}
