import { zodResolver } from '@hookform/resolvers/zod';
import { CalendarPlus } from 'lucide-react';
import { useMemo, type Ref } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useEditDistribution, usePlanDistribution } from '@/api/generated/distribution/distribution';
import type { DistributionDayResponse, DistributionType } from '@/api/generated/model';
import { DateInput } from '@/components/app/DateInput';
import { EditSheet, type EditResult } from '@/components/app/EditSheet';
import { FormField } from '@/components/app/FormField';
import { TextInput } from '@/components/app/TextInput';
import { useAppForm } from '@/components/app/use-app-form';
import { applyFieldErrors, isStale, problemMessage, sameNames } from '../problems';
import { useDistributionRefresh } from '../queries';
import { daySchema, EMPTY_DAY, MAX_LOCATION_LENGTH, type DayInput, type DayValues } from '../schemas';

type DayForm = UseFormReturn<DayValues, unknown, DayInput>;

const FIELDS = sameNames<DayValues>('date', 'location');

function DayFields({ form }: { form: DayForm }) {
  const { t } = useTranslation('distribution');
  return (
    <>
      <FormField control={form.control} name="date" label={t('day.date')} width="short">
        {(field) => <DateInput {...field} />}
      </FormField>
      <FormField control={form.control} name="location" label={t('day.location')} width="long">
        {(field) => <TextInput {...field} maxLength={MAX_LOCATION_LENGTH} autoComplete="off" />}
      </FormField>
    </>
  );
}

/** The panel's values for a day as the server has it, or empty while it is not planned. */
const valuesOf = (day: DistributionDayResponse | undefined): DayValues =>
  day ? { date: day.date, location: day.location } : EMPTY_DAY;

/**
 * Saves a day and refreshes the distribution; field errors land on the panel's fields. On a
 * conflict the panel shows the day as it is now, so the person reviews it before saving again.
 */
function useSaveDay(editionId: string, type: DistributionType, form: DayForm) {
  const { t } = useTranslation('distribution');
  const refresh = useDistributionRefresh(editionId);
  return async (write: () => Promise<unknown>): Promise<EditResult> => {
    try {
      await write();
    } catch (error) {
      if (isStale(error)) {
        const plan = await refresh();
        if (plan) form.reset(valuesOf(plan.days.find((day) => day.type === type)));
        return { status: 'conflict', reason: problemMessage(t, error) };
      }
      return applyFieldErrors(error, FIELDS, form.setError)
        ? { status: 'kept' }
        : { status: 'rejected', reason: problemMessage(t, error) };
    }
    await refresh();
    return { status: 'saved' };
  };
}

interface DaySheetProps {
  editionId: string;
  type: DistributionType;
  /** The planned day to edit; none to plan it. */
  day?: DistributionDayResponse;
  /** The "Plan" or "Edit" button, e.g. to focus it once the day is deleted. */
  triggerRef?: Ref<HTMLButtonElement>;
  /** Hides the button while the day cannot be changed, keeping an open panel and its reason. */
  hideTrigger?: boolean;
}

/**
 * An Admin plans or edits a distribution day's date and location in a side panel (spec:
 * Distribution days): "Plan" while it is not planned, "Edit" once it is.
 */
export function DaySheet({ editionId, type, day, triggerRef, hideTrigger }: DaySheetProps) {
  const { t } = useTranslation('distribution');
  const plan = usePlanDistribution();
  const edit = useEditDistribution();
  const values = useMemo(() => valuesOf(day), [day]);
  const form = useAppForm<DayValues, unknown, DayInput>({
    resolver: zodResolver(daySchema),
    defaultValues: values,
  });
  const save = useSaveDay(editionId, type, form);

  // One panel for both: once a day is planned, its trigger turns from "Plan" into "Edit" in place,
  // so focus returns to it and the notice is announced.
  return (
    <EditSheet
      title={t(day ? `day.editTitle.${type}` : `day.planTitle.${type}`)}
      sectionName={t(`day.name.${type}`)}
      triggerRef={triggerRef}
      hideTrigger={hideTrigger}
      trigger={day ? undefined : { label: t('day.plan'), icon: CalendarPlus, context: t(`day.name.${type}`) }}
      form={form}
      values={values}
      savedText={t(day ? 'day.saved' : 'day.planned')}
      onSave={(submitted) =>
        save(() =>
          day
            ? edit.mutateAsync({ id: day.id, data: { ...submitted, version: day.version } })
            : plan.mutateAsync({ editionId, data: { type, ...submitted } }),
        )
      }
    >
      <DayFields form={form} />
    </EditSheet>
  );
}
