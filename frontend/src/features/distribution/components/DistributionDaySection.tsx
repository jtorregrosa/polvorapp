import { Trash2 } from 'lucide-react';
import { useMemo, useRef, type RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import {
  getDownloadDistributionListUrl,
  useDeleteDistribution,
} from '@/api/generated/distribution/distribution';
import type {
  DistributionDayResponse,
  DistributionPlanResponse,
  DistributionType,
  NotValidatedResponse,
} from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { explainFailure } from '@/components/app/confirm-failure';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { Disclosure } from '@/components/app/Disclosure';
import { KeyFacts } from '@/components/app/KeyFacts';
import { useSaveNotice } from '@/components/app/save-notice';
import { SectionCard } from '@/components/app/SectionCard';
import { DownloadButtons } from '@/features/exports/components/DownloadButtons';
import { useEditionDates } from '@/features/festival-editions/dates';
import { isStale, problemCode, problemMessage } from '../problems';
import { useDistributionRefresh } from '../queries';
import { DaySheet } from './DaySheet';
import { SlotsSheet } from './SlotsSheet';

/** A row of the slots table: a comparsa and its start time, or none. */
interface SlotRow {
  comparsaId: string;
  comparsaName: string;
  startsAt: string | null;
}

/** Slots in time order, then name (spec: Distribution screens). */
function slotRows(day: DistributionDayResponse, locale: string): SlotRow[] {
  return [...day.slots].sort(
    (a, b) => a.startsAt.localeCompare(b.startsAt) || a.comparsaName.localeCompare(b.comparsaName, locale),
  );
}

/** For Admins, the comparsas without a slot, by name, folded under their count (UI audit). */
function WithoutSlot({ day }: { day: DistributionDayResponse }) {
  const { t, i18n } = useTranslation('distribution');
  const names = useMemo(
    () =>
      (day.withoutSlot ?? [])
        .map((comparsa) => comparsa.name)
        .sort((a, b) => a.localeCompare(b, i18n.language)),
    [day.withoutSlot, i18n.language],
  );
  if (names.length === 0) return null;
  return (
    <Disclosure summary={t('slots.withoutSlot', { count: names.length })}>
      {/* eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`. */}
      <ul role="list" className="grid gap-x-4 gap-y-1 text-muted-foreground sm:grid-cols-2">
        {names.map((name) => (
          <li key={name}>{name}</li>
        ))}
      </ul>
    </Disclosure>
  );
}

/** For FiringChiefs: the day, the place and their slot in one line per comparsa (UI audit). */
function OwnSlots({ day }: { day: DistributionDayResponse }) {
  const { t } = useTranslation('distribution');
  const dates = useEditionDates();
  const date = dates.day(day.date);
  if (day.slots.length === 0) {
    return (
      <p className="text-body text-foreground">{t('slots.noSlotYet', { date, location: day.location })}</p>
    );
  }
  return (
    // eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`.
    <ul role="list" className="flex flex-col gap-1 text-body font-semibold text-foreground">
      {day.slots.map((slot) => (
        <li key={slot.comparsaId}>
          {day.slots.length === 1
            ? t('slots.yourSlot', { date, time: slot.startsAt, location: day.location })
            : t('slots.comparsaSlot', {
                comparsa: slot.comparsaName,
                date,
                time: slot.startsAt,
                location: day.location,
              })}
        </li>
      ))}
    </ul>
  );
}

function Slots({ day }: { day: DistributionDayResponse }) {
  const { t, i18n } = useTranslation('distribution');
  const rows = useMemo(() => slotRows(day, i18n.language), [day, i18n.language]);
  const columns = useMemo<DataTableColumn<SlotRow>[]>(
    () => [
      {
        id: 'startsAt',
        header: t('slots.columns.startsAt'),
        cell: (row) => row.startsAt ?? <span className="text-muted-foreground">{t('slots.noSlot')}</span>,
      },
      {
        id: 'comparsa',
        header: t('slots.columns.comparsa'),
        rowHeader: true,
        cell: (row) => row.comparsaName,
      },
    ],
    [t],
  );
  return (
    <DataTable
      caption={t(`slots.caption.${day.type}`)}
      data={rows}
      columns={columns}
      paginated={false}
      getRowId={(row) => row.comparsaId}
      emptyText={t('slots.empty')}
      mobileRow={(row) => (
        <>
          <span className="font-semibold text-foreground">{row.comparsaName}</span>
          <span className="text-help text-muted-foreground">
            <span className="sr-only">{t('slots.columns.startsAt')}: </span>
            {row.startsAt ?? t('slots.noSlot')}
          </span>
        </>
      )}
    />
  );
}

/** Where a comparsa's order stands; never validated here. */
function notValidatedStatus({ status }: NotValidatedResponse) {
  return status === null || status === 'VALIDATED' ? 'NOT_PREPARED' : status;
}

/** The comparsas whose orders are not validated, so not on the lists. */
function NotValidated({ comparsas }: { comparsas: readonly NotValidatedResponse[] }) {
  const { t } = useTranslation('distribution');
  return (
    <AlertBanner severity="warning" live={false} title={t('list.notValidated.title')}>
      <p>{t('list.notValidated.body', { count: comparsas.length })}</p>
      {/* A long list would push the downloads far down: folded under one line (UI audit). */}
      <Disclosure summary={t('list.notValidated.show', { count: comparsas.length })}>
        <ul className="list-disc ps-5">
          {comparsas.map((comparsa) => (
            <li key={comparsa.comparsaId}>
              {t('list.notValidated.item', {
                comparsa: comparsa.comparsaName,
                status: t(`list.notValidated.status.${notValidatedStatus(comparsa)}`),
              })}
            </li>
          ))}
        </ul>
      </Disclosure>
    </AlertBanner>
  );
}

/** For Admins: the day's list as Excel and PDF, and what it leaves out (spec: Distribution lists). */
function DayList({
  day,
  notValidated,
}: {
  day: DistributionDayResponse;
  notValidated: readonly NotValidatedResponse[];
}) {
  const { t } = useTranslation('distribution');
  return (
    <div className="flex flex-col gap-3">
      <h3 className="text-label text-foreground">{t('list.title')}</h3>
      <p className="text-help text-muted-foreground">{t('list.numbering')}</p>
      {notValidated.length > 0 && <NotValidated comparsas={notValidated} />}
      <DownloadButtons
        what={t(`list.what.${day.type}`)}
        urls={{
          xlsx: getDownloadDistributionListUrl(day.id, 'xlsx'),
          pdf: getDownloadDistributionListUrl(day.id, 'pdf'),
        }}
      />
    </div>
  );
}

function DeleteDay({
  day,
  editionId,
  dayTrigger,
}: {
  day: DistributionDayResponse;
  editionId: string;
  /** The day's "Edit" button, which becomes "Plan" in place: focus goes there once the day is gone. */
  dayTrigger: RefObject<HTMLButtonElement | null>;
}) {
  const { t } = useTranslation('distribution');
  const remove = useDeleteDistribution();
  const refresh = useDistributionRefresh(editionId);
  const notify = useSaveNotice();
  return (
    <ConfirmDialog
      title={t(`day.deleteTitle.${day.type}`)}
      description={t('day.deleteDescription')}
      confirmLabel={t('day.deleteConfirm')}
      onConfirm={() =>
        explainFailure(
          async () => {
            try {
              await remove.mutateAsync({ id: day.id, params: { version: day.version } });
            } catch (error) {
              // Already deleted elsewhere: that is what the Admin wanted.
              if (problemCode(error) === 'distribution.notFound') return;
              // Changed meanwhile: show the day as it is now, as the reason says.
              if (isStale(error)) await refresh();
              throw error;
            }
          },
          (error) => problemMessage(t, error),
        )
      }
      onConfirmed={() => {
        // The "Delete" button goes away with the day (WCAG 2.4.3).
        dayTrigger.current?.focus();
        notify(t('day.deleted'));
        void refresh();
      }}
      trigger={
        <Button
          variant="quietDestructive"
          size="sm"
          icon={Trash2}
          aria-label={t(`day.deleteName.${day.type}`)}
        >
          {t('day.delete')}
        </Button>
      }
    />
  );
}

interface DistributionDaySectionProps {
  plan: DistributionPlanResponse;
  type: DistributionType;
  /** Admins download the lists, also once the edition is closed. */
  isAdmin: boolean;
}

/**
 * One distribution day of an edition (spec: Distribution screens): its date and location or that it
 * is not planned, its slots, and for Admins its list; planning, editing and deleting while the
 * edition is in progress (`canPlan`).
 */
export function DistributionDaySection({ plan, type, isAdmin }: DistributionDaySectionProps) {
  const { t } = useTranslation('distribution');
  const dates = useEditionDates();
  const day = plan.days.find((candidate) => candidate.type === type);
  const dayTrigger = useRef<HTMLButtonElement>(null);

  // Admins keep the panels mounted (hidden triggers) once the edition stops being in progress, so an
  // open panel still shows why its save was refused.
  const actions = isAdmin && (
    <div className="flex flex-wrap justify-end gap-2">
      <DaySheet
        editionId={plan.editionId}
        type={type}
        day={day}
        triggerRef={dayTrigger}
        hideTrigger={!plan.canPlan}
      />
      {day && plan.canPlan && <DeleteDay day={day} editionId={plan.editionId} dayTrigger={dayTrigger} />}
    </div>
  );

  return (
    <SectionCard title={t(`day.title.${type}`)} action={actions}>
      {day && !isAdmin ? (
        <OwnSlots day={day} />
      ) : day ? (
        <>
          <KeyFacts
            label={t(`day.facts.${type}`)}
            items={[
              { id: 'date', label: t('day.date'), value: dates.day(day.date) },
              { id: 'location', label: t('day.location'), value: day.location },
            ]}
          />
          <div className="flex flex-col gap-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h3 className="text-label text-foreground">{t('slots.title')}</h3>
              {isAdmin && <SlotsSheet editionId={plan.editionId} day={day} hideTrigger={!plan.canPlan} />}
            </div>
            <Slots day={day} />
            <WithoutSlot day={day} />
          </div>
          {isAdmin && <DayList day={day} notValidated={plan.notValidated ?? []} />}
        </>
      ) : (
        <p className="text-muted-foreground">{t('day.notPlanned')}</p>
      )}
    </SectionCard>
  );
}
