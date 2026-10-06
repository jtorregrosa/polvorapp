import { Trash2 } from 'lucide-react';
import { zodResolver } from '@hookform/resolvers/zod';
import type { RowData } from '@tanstack/react-table';
import { createContext, use, useMemo, useState, type ReactNode } from 'react';
import { useAppForm } from '@/components/app/use-app-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { explainFailure } from '@/components/app/confirm-failure';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { Form, FormField } from '@/components/app/FormField';
import { SectionCard } from '@/components/app/SectionCard';
import { SelectInput, type SelectOption } from '@/components/app/SelectInput';
import type { Notice } from '@/lib/notices';
import { messages, problemMessage } from '../problems';

const addSchema = z.object({ candidate: z.string().min(1, messages.choice) });
type AddValues = z.infer<typeof addSchema>;

/** Someone or something that can be added: `label` is what the select shows, `name` what notices say. */
export interface AssignmentCandidate {
  id: string;
  name: string;
  label: string;
}

export interface AssignmentListText {
  title: string;
  /** Names the table, distinct from the section's title, e.g. "Firing chiefs of Comparsa Norte". */
  caption: string;
  description: string;
  emptyText: string;
  addLabel: string;
  add: string;
  noCandidates: string;
  /** Replaces the add control, e.g. "Reactivate the comparsa to assign new firing chiefs.". */
  addBlocked?: string;
  added: (name: string) => string;
  removed: (name: string) => string;
  /** Accessible name of a row's remove button; starts with {@link removeShort}. */
  remove: (name: string) => string;
  /** What the remove button shows: short, so the table fits a phone (WCAG 2.5.3: in the name). */
  removeShort: string;
  removeTitle: (name: string) => string;
  removeDescription: string;
}

export interface AssignmentListProps<TRow extends RowData> {
  text: AssignmentListText;
  rows: readonly TRow[];
  /** Keep stable (useMemo): the remove column is added after them. */
  columns: readonly DataTableColumn<TRow>[];
  getRowId: (row: TRow) => string;
  getRowName: (row: TRow) => string;
  /** The row as a stacked item on phones (name first); its remove button is added after it. */
  describeRow: (row: TRow) => ReactNode;
  isLoading: boolean;
  /** A failed load of the rows or the candidates: shown instead of a misleading empty state. */
  error?: unknown;
  /** Who or what can be added; already filtered (spec: Candidates exclude ineligible users and comparsas). */
  candidates: readonly AssignmentCandidate[];
  onAdd: (candidateId: string) => Promise<unknown>;
  onRemove: (row: TRow) => Promise<unknown>;
  /** Refreshes both sides of the assignment after a change or a rejected one. */
  onChanged: () => Promise<unknown>;
}

interface RowActions {
  text: AssignmentListText;
  getRowName: (row: unknown) => string;
  onRemove: (row: unknown) => Promise<unknown>;
  onChanged: () => Promise<unknown>;
  announce: (severity: Notice['severity'], text: string) => void;
}

/**
 * What the remove button of a row needs. Read from context, not captured by the column
 * definition: the columns then never change, so a re-render (e.g. the request going pending)
 * never remounts a row and its open confirmation dialog.
 */
const RowActionsContext = createContext<RowActions | undefined>(undefined);

function RemoveCell({ row }: { row: unknown }) {
  const { t } = useTranslation('catalog');
  const actions = use(RowActionsContext);
  if (!actions) {
    throw new Error('RemoveCell renders inside AssignmentList.');
  }
  const { text, getRowName, onRemove, onChanged, announce } = actions;
  const name = getRowName(row);
  return (
    <ConfirmDialog
      title={text.removeTitle(name)}
      description={text.removeDescription}
      confirmLabel={text.remove(name)}
      onConfirm={() =>
        explainFailure(
          () => onRemove(row),
          (error) => problemMessage(t, error),
        )
      }
      onConfirmed={() => {
        announce('success', text.removed(name));
        void onChanged();
      }}
      trigger={
        <Button
          type="button"
          variant="quietDestructive"
          size="sm"
          icon={Trash2}
          aria-label={text.remove(name)}
        >
          {text.removeShort}
        </Button>
      }
    />
  );
}

/**
 * The FiringChiefs of a comparsa, or the comparsas of a FiringChief (spec: Managing assignments
 * from the comparsa and from the user): a short list with a confirmed remove per row and an add
 * control. The section owns its outcome notice, focused when shown, so focus is never lost when
 * a removed row disappears (WCAG 2.4.3).
 */
export function AssignmentList<TRow extends RowData>({
  text,
  rows,
  columns,
  getRowId,
  getRowName,
  describeRow,
  isLoading,
  error,
  candidates,
  onAdd,
  onRemove,
  onChanged,
}: AssignmentListProps<TRow>) {
  const { t } = useTranslation('catalog');
  const [notice, setNotice] = useState<Notice>();
  const announce = (severity: Notice['severity'], message: string): void => {
    setNotice((previous) => ({ id: (previous?.id ?? 0) + 1, severity, text: message }));
  };
  const form = useAppForm<AddValues>({ resolver: zodResolver(addSchema), defaultValues: { candidate: '' } });

  const add = async ({ candidate }: AddValues): Promise<void> => {
    const name = candidates.find((option) => option.id === candidate)?.name ?? '';
    try {
      await onAdd(candidate);
      form.reset({ candidate: '' });
      announce('success', text.added(name));
    } catch (failure) {
      announce('error', problemMessage(t, failure));
    }
    await onChanged();
  };

  const options = useMemo<SelectOption[]>(
    () => candidates.map(({ id, label }) => ({ value: id, label })),
    [candidates],
  );

  const actionsHeader = t('comparsas.actions.title');
  const allColumns = useMemo<DataTableColumn<TRow>[]>(
    () => [
      ...columns,
      {
        id: 'remove',
        header: actionsHeader,
        hideHeader: true,
        pinned: true,
        align: 'end',
        cell: (row) => <RemoveCell row={row} />,
      },
    ],
    [columns, actionsHeader],
  );

  const rowActions: RowActions = {
    text,
    getRowName: getRowName as (row: unknown) => string,
    onRemove: onRemove as (row: unknown) => Promise<unknown>,
    onChanged,
    announce,
  };

  const addControl = text.addBlocked ? (
    <p className="text-help text-muted-foreground">{text.addBlocked}</p>
  ) : candidates.length === 0 ? (
    <p className="text-help text-muted-foreground">{text.noCandidates}</p>
  ) : (
    <Form form={form} onSubmit={add} requiredNote={false} className="flex flex-wrap items-end gap-3">
      <FormField control={form.control} name="candidate" label={text.addLabel} width="long">
        {(field) => <SelectInput {...field} placeholder={t('validation.choice')} options={options} />}
      </FormField>
      {/* Level with the select, whether or not an error shows between its label and it. */}
      <Button type="submit" variant="secondary" pending={form.formState.isSubmitting}>
        {text.add}
      </Button>
    </Form>
  );

  const failed = error !== undefined && error !== null;

  return (
    <SectionCard span="full" title={text.title} description={text.description}>
      {notice && (
        <AlertBanner key={notice.id} severity={notice.severity} focusOnMount>
          {notice.text}
        </AlertBanner>
      )}
      {failed ? (
        <AlertBanner severity="error">{problemMessage(t, error)}</AlertBanner>
      ) : (
        <>
          <RowActionsContext value={rowActions}>
            <DataTable
              caption={text.caption}
              data={rows}
              columns={allColumns}
              getRowId={getRowId}
              mobileRow={(row) => (
                <>
                  {describeRow(row)}
                  <span>
                    <RemoveCell row={row} />
                  </span>
                </>
              )}
              isLoading={isLoading}
              paginated={false}
              emptyText={text.emptyText}
            />
          </RowActionsContext>
          {!isLoading && addControl}
        </>
      )}
    </SectionCard>
  );
}
