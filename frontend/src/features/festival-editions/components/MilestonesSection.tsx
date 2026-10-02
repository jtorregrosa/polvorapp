import { zodResolver } from '@hookform/resolvers/zod';
import { Plus, Trash2 } from 'lucide-react';
import { useMemo, useRef, type RefObject } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import {
  useAddCalendarMilestone,
  useRemoveCalendarMilestone,
  useUpdateCalendarMilestone,
} from '@/api/generated/editions/editions';
import type { CalendarMilestoneResponse, EditionResponse } from '@/api/generated/model';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { explainFailure } from '@/components/app/confirm-failure';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { DateInput } from '@/components/app/DateInput';
import { EditSheet, type EditResult } from '@/components/app/EditSheet';
import { FormField } from '@/components/app/FormField';
import { useSaveNotice } from '@/components/app/save-notice';
import { SectionCard } from '@/components/app/SectionCard';
import { TextInput } from '@/components/app/TextInput';
import { useAppForm } from '@/components/app/use-app-form';
import { useEditionDates } from '../dates';
import {
  EMPTY_MILESTONE,
  milestoneSchema,
  type MilestoneInput,
  type MilestoneValues,
} from '../editionSchema';
import { applyFieldErrors, problemCode } from '../problems';
import { useEditionRefresh, useExplain } from './useSaveEdition';

const FIELDS = ['date', 'title'] as const;

function MilestoneFields({ form }: { form: UseFormReturn<MilestoneValues, unknown, MilestoneInput> }) {
  const { t } = useTranslation('editions');
  return (
    <>
      <FormField control={form.control} name="date" label={t('sections.milestones.date')} width="short">
        {(field) => <DateInput {...field} />}
      </FormField>
      <FormField control={form.control} name="title" label={t('sections.milestones.titleLabel')} width="long">
        {(field) => <TextInput {...field} maxLength={100} autoComplete="off" />}
      </FormField>
    </>
  );
}

/** Saves a milestone and refreshes the edition; field errors land on the panel's fields. */
function useSaveMilestone(editionId: string, form: UseFormReturn<MilestoneValues, unknown, MilestoneInput>) {
  const refresh = useEditionRefresh(editionId);
  const explain = useExplain();
  return async (write: () => Promise<unknown>): Promise<EditResult> => {
    try {
      await write();
    } catch (error) {
      const code = problemCode(error);
      if (code === 'editions.notFound' || code === 'editions.milestoneNotFound') {
        await refresh();
        return { status: 'conflict', reason: explain(error) };
      }
      return applyFieldErrors(error, FIELDS, form.setError)
        ? { status: 'kept' }
        : { status: 'rejected', reason: explain(error) };
    }
    await refresh();
    return { status: 'saved' };
  };
}

function AddMilestone({
  editionId,
  triggerRef,
}: {
  editionId: string;
  triggerRef: RefObject<HTMLButtonElement | null>;
}) {
  const { t } = useTranslation('editions');
  const add = useAddCalendarMilestone();
  const form = useAppForm<MilestoneValues, unknown, MilestoneInput>({
    resolver: zodResolver(milestoneSchema),
    defaultValues: EMPTY_MILESTONE,
  });
  const save = useSaveMilestone(editionId, form);
  return (
    <EditSheet
      triggerRef={triggerRef}
      title={t('sections.milestones.addTitle')}
      sectionName={t('sections.milestones.title')}
      trigger={{ label: t('sections.milestones.add'), icon: Plus }}
      form={form}
      values={EMPTY_MILESTONE}
      savedText={t('sections.milestones.added')}
      onSave={(submitted) => save(() => add.mutateAsync({ id: editionId, data: submitted }))}
    >
      <MilestoneFields form={form} />
    </EditSheet>
  );
}

function MilestoneActions({
  editionId,
  milestone,
  addRef,
}: {
  editionId: string;
  milestone: CalendarMilestoneResponse;
  /** "Add milestone", where focus goes once this row is removed. */
  addRef: RefObject<HTMLButtonElement | null>;
}) {
  const { t } = useTranslation('editions');
  const update = useUpdateCalendarMilestone();
  const remove = useRemoveCalendarMilestone();
  const refresh = useEditionRefresh(editionId);
  const explain = useExplain();
  const notify = useSaveNotice();
  const values = useMemo<MilestoneValues>(
    () => ({ date: milestone.date, title: milestone.title }),
    [milestone],
  );
  const form = useAppForm<MilestoneValues, unknown, MilestoneInput>({
    resolver: zodResolver(milestoneSchema),
    defaultValues: values,
  });
  const save = useSaveMilestone(editionId, form);
  return (
    <span className="flex flex-wrap justify-end gap-2">
      <EditSheet
        title={t('sections.milestones.editTitle')}
        sectionName={t('sections.milestones.editName', { title: milestone.title })}
        form={form}
        values={values}
        onSave={(submitted) =>
          save(() => update.mutateAsync({ id: editionId, milestoneId: milestone.id, data: submitted }))
        }
      >
        <MilestoneFields form={form} />
      </EditSheet>
      <ConfirmDialog
        title={t('sections.milestones.removeTitle', { title: milestone.title })}
        description={t('sections.milestones.removeDescription')}
        confirmLabel={t('sections.milestones.removeConfirm')}
        onConfirm={() =>
          explainFailure(() => remove.mutateAsync({ id: editionId, milestoneId: milestone.id }), explain)
        }
        onConfirmed={() => {
          // The row goes away with its buttons: focus "Add milestone" instead (WCAG 2.4.3).
          addRef.current?.focus();
          notify(t('sections.milestones.removed'));
          void refresh();
        }}
        trigger={
          <Button
            variant="secondary"
            size="sm"
            icon={Trash2}
            aria-label={t('sections.milestones.removeName', { title: milestone.title })}
          >
            {t('sections.milestones.remove')}
          </Button>
        }
      />
    </span>
  );
}

/** The edition's calendar milestones, by date (spec: Calendar milestones). */
export function MilestonesSection({ edition, canEdit }: { edition: EditionResponse; canEdit: boolean }) {
  const { t } = useTranslation('editions');
  const dates = useEditionDates();
  const addRef = useRef<HTMLButtonElement>(null);
  const columns = useMemo<DataTableColumn<CalendarMilestoneResponse>[]>(
    () => [
      {
        id: 'date',
        header: t('sections.milestones.columns.date'),
        cell: (milestone) => dates.day(milestone.date),
      },
      {
        id: 'title',
        header: t('sections.milestones.columns.title'),
        rowHeader: true,
        cell: (milestone) => milestone.title,
      },
      ...(canEdit
        ? [
            {
              id: 'actions',
              header: t('sections.milestones.columns.actions'),
              hideHeader: true,
              align: 'end' as const,
              cell: (milestone: CalendarMilestoneResponse) => (
                <MilestoneActions editionId={edition.id} milestone={milestone} addRef={addRef} />
              ),
            },
          ]
        : []),
    ],
    [t, dates, canEdit, edition.id, addRef],
  );

  return (
    <SectionCard
      title={t('sections.milestones.title')}
      span="full"
      action={canEdit && <AddMilestone editionId={edition.id} triggerRef={addRef} />}
    >
      <DataTable
        caption={t('sections.milestones.caption')}
        data={edition.milestones}
        columns={columns}
        paginated={false}
        getRowId={(milestone) => milestone.id}
        emptyText={t('sections.milestones.empty')}
        mobileRow={(milestone) => (
          <>
            <span className="font-semibold text-foreground">{milestone.title}</span>
            <span className="text-help text-muted-foreground">{dates.day(milestone.date)}</span>
            {canEdit && <MilestoneActions editionId={edition.id} milestone={milestone} addRef={addRef} />}
          </>
        )}
      />
    </SectionCard>
  );
}
