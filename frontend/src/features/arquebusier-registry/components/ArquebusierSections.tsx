import { zodResolver } from '@hookform/resolvers/zod';
import { useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateArquebusier } from '@/api/generated/arquebusiers/arquebusiers';
import type { ArquebusierResponse } from '@/api/generated/model';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { DescriptionList } from '@/components/app/DescriptionList';
import { EditSheet, type EditResult } from '@/components/app/EditSheet';
import { SectionCard } from '@/components/app/SectionCard';
import { SectionGrid } from '@/components/app/SectionGrid';
import { useAppForm } from '@/components/app/use-app-form';
import { useSession } from '@/features/identity-access/session';
import { useFormatters } from '@/lib/format';
import type { Announce } from '@/lib/notices';
import {
  ARQUEBUSIER_FIELDS,
  requestFields,
  SECTION_FIELDS,
  SECTION_SCHEMAS,
  valuesOf,
  type ArquebusierValues,
} from '../arquebusierSchema';
import { useRefreshArquebusier, useRefreshRegistryViews } from '../hooks';
import { CourseFields, LicenseFields, PersonalFields } from '../pages/ArquebusierFields';
import { applyFieldErrors, problemCode, problemMessage } from '../problems';
import { LicensePhotos } from './ArquebusierPhotos';
import { OwnedWeaponsSection } from './OwnedWeaponsSection';

type EditableSection = keyof typeof SECTION_SCHEMAS;

const DATE: Intl.DateTimeFormatOptions = { day: '2-digit', month: '2-digit', year: 'numeric' };

/** The record with one section's values put in: the rest stays as loaded (design D9). */
function merged(arquebusier: ArquebusierResponse, section: EditableSection, values: ArquebusierValues) {
  const stored = valuesOf(arquebusier);
  const fields: readonly (keyof ArquebusierValues)[] = SECTION_FIELDS[section];
  return Object.fromEntries(
    Object.entries(stored).map(([key, value]) => [
      key,
      fields.includes(key as keyof ArquebusierValues) ? values[key as keyof ArquebusierValues] : value,
    ]),
  ) as ArquebusierValues;
}

interface SaverOptions {
  arquebusier: ArquebusierResponse;
  /** Loads the record again and returns it, for a version conflict. */
  reload: () => Promise<ArquebusierResponse | undefined>;
}

interface SectionsProps extends SaverOptions {
  announce: Announce;
  /** False while the registry is locked for the caller (BR-10): read only. */
  canWrite: boolean;
}

/**
 * Saves one section with the full update and its version (design D9): field errors from the server
 * go to the fields, a version conflict reloads the record and keeps the panel open to review it.
 */
function useSaveSection({ arquebusier, reload }: SaverOptions) {
  const { t } = useTranslation('registry');
  const isAdmin = useSession().account?.role === 'ADMIN';
  const update = useUpdateArquebusier();
  const refresh = useRefreshArquebusier(arquebusier.id);
  const refreshViews = useRefreshRegistryViews();

  return async (
    section: EditableSection,
    form: ReturnType<typeof useAppForm<ArquebusierValues>>,
    values: ArquebusierValues,
    notice?: string,
  ): Promise<EditResult> => {
    const record = merged(arquebusier, section, values);
    try {
      await update.mutateAsync({
        id: arquebusier.id,
        data: { ...requestFields(record), version: arquebusier.version },
      });
    } catch (error) {
      const code = problemCode(error);
      if (code === 'arquebusiers.modified') {
        // Spec: someone else changed the record; show its current values to review. Its warnings
        // may have changed too, so the list and the insights follow.
        refreshViews();
        const fresh = await reload();
        // Without the current values the form keeps what was typed: resetting it to the stale
        // record would hide the other change and the next save would conflict again.
        if (!fresh) return { status: 'rejected', reason: `${t('form.modified')} ${t('load.stale')}` };
        form.reset(valuesOf(fresh));
        return { status: 'conflict', reason: t('form.modified') };
      }
      if (code === 'arquebusiers.notFound') {
        await refresh();
        return { status: 'rejected', reason: problemMessage(t, error) };
      }
      const placed = applyFieldErrors(error, ARQUEBUSIER_FIELDS, form.setError, {
        isAdmin,
        conflicts: {
          'arquebusiers.nationalIdTaken': 'nationalId',
          'arquebusiers.federationIdTaken': 'federationId',
        },
      });
      return placed
        ? { status: 'kept' }
        : { status: 'rejected', reason: problemMessage(t, error, { isAdmin }) };
    }
    await refresh();
    return { status: 'saved', notice };
  };
}

function useSectionForm(section: EditableSection, arquebusier: ArquebusierResponse) {
  return useAppForm<ArquebusierValues>({
    resolver: zodResolver(SECTION_SCHEMAS[section]),
    defaultValues: valuesOf(arquebusier),
  });
}

type Save = ReturnType<typeof useSaveSection>;

interface SectionProps {
  arquebusier: ArquebusierResponse;
  /** The record's values, where each panel starts. */
  values: ArquebusierValues;
  save: Save;
  formatDate: (iso: string | null) => string;
  /** False while the registry is locked for the caller (BR-10): read only. */
  canWrite: boolean;
}

function PersonalSection({ arquebusier, values, save, formatDate, canWrite }: SectionProps) {
  const { t } = useTranslation('registry');
  const personal = useSectionForm('personal', arquebusier);
  return (
    <SectionCard
      title={t('form.personal')}
      action={
        <EditSheet
          hideTrigger={!canWrite}
          title={t('detail.sections.personal.edit')}
          description={t('form.personalDescription')}
          sectionName={t('detail.sections.personal.name')}
          form={personal}
          values={values}
          onSave={(submitted) => save('personal', personal, submitted)}
        >
          <PersonalFields form={personal} />
        </EditSheet>
      }
    >
      <DescriptionList
        items={[
          { term: t('form.firstName'), value: arquebusier.firstName },
          { term: t('form.lastName'), value: arquebusier.lastName },
          { term: t('form.nationalId'), value: arquebusier.nationalId, mono: true },
          { term: t('form.federationId'), value: String(arquebusier.federationId), mono: true },
          { term: t('form.birthDate'), value: formatDate(arquebusier.birthDate) },
          { term: t('form.gender'), value: t(`gender.${arquebusier.gender}`) },
          { term: t('form.email'), value: arquebusier.email },
          { term: t('form.phone'), value: arquebusier.phone },
        ]}
      />
    </SectionCard>
  );
}

/**
 * Spec "Current license": removing a license erases its photos, so the person confirms it first.
 * `ask` resolves to their answer; focus then returns to what asked (the panel's Save button).
 */
function useRemovalConfirmation() {
  const { t } = useTranslation('registry');
  const [settle, setSettle] = useState<(confirmed: boolean) => void>();
  const opener = useRef<HTMLElement | null>(null);
  const ask = () =>
    new Promise<boolean>((resolve) => {
      opener.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
      setSettle(() => resolve);
    });
  const answer = (confirmed: boolean) => {
    settle?.(confirmed);
    setSettle(undefined);
  };
  const dialog = (
    <ConfirmDialog
      open={settle !== undefined}
      onOpenChange={(open) => {
        if (!open) answer(false);
      }}
      returnFocus={opener}
      title={t('license.removePhotosConfirm.title')}
      description={t('license.removePhotosConfirm.description')}
      confirmLabel={t('license.removePhotosConfirm.confirm')}
      onConfirm={() => {
        answer(true);
      }}
    />
  );
  return { ask, dialog };
}

function LicenseSection({
  arquebusier,
  values,
  save,
  formatDate,
  refresh,
  canWrite,
}: SectionProps & { refresh: () => Promise<void> }) {
  const { t } = useTranslation('registry');
  const license = useSectionForm('license', arquebusier);
  const removal = useRemovalConfirmation();
  const [resets, setResets] = useState(0);
  const hasLicensePhotos =
    arquebusier.photos.licenseFront !== null || arquebusier.photos.licenseBack !== null;

  const saveLicense = async (submitted: ArquebusierValues): Promise<EditResult> => {
    if (hasLicensePhotos && arquebusier.license !== null && submitted.licenseType === '') {
      if (!(await removal.ask())) return { status: 'kept' };
    }
    const renewed =
      hasLicensePhotos &&
      submitted.licenseType !== '' &&
      (['licenseType', 'licensePending', 'issuedOn', 'expiresOn'] as const).some(
        (field) => values[field] !== submitted[field],
      );
    // A renewal keeps the old license photos until they are replaced: remind the user.
    const result = await save(
      'license',
      license,
      submitted,
      renewed ? t('form.savedReplacePhotos') : undefined,
    );
    // The fields were reset to the server's values: start the expiry auto-fill again from them.
    if (result.status === 'conflict') setResets((count) => count + 1);
    return result;
  };

  const stored = arquebusier.license;
  return (
    <>
      <SectionCard
        title={t('form.license')}
        action={
          <EditSheet
            hideTrigger={!canWrite}
            title={t('detail.sections.license.edit')}
            description={t('form.licenseDescription')}
            sectionName={t('detail.sections.license.name')}
            form={license}
            values={values}
            onSave={saveLicense}
          >
            <LicenseFields key={resets} form={license} />
          </EditSheet>
        }
      >
        <DescriptionList
          items={[
            {
              term: t('form.licenseType'),
              value: stored ? t(`licenseType.${stored.type}`) : t('form.noLicense'),
            },
            ...(stored?.pending
              ? [{ term: t('form.pending'), value: t('detail.values.pending') }]
              : stored
                ? [
                    { term: t('form.issuedOn'), value: formatDate(stored.issuedOn) },
                    { term: t('form.expiresOn'), value: formatDate(stored.expiresOn) },
                  ]
                : []),
          ]}
        />
        <LicensePhotos arquebusier={arquebusier} onChanged={refresh} canWrite={canWrite} />
      </SectionCard>
      {removal.dialog}
    </>
  );
}

function CourseSection({ arquebusier, values, save, formatDate, canWrite }: SectionProps) {
  const { t } = useTranslation('registry');
  const course = useSectionForm('course', arquebusier);
  return (
    <SectionCard
      title={t('form.training')}
      action={
        <EditSheet
          hideTrigger={!canWrite}
          title={t('detail.sections.course.edit')}
          description={t('form.trainingDescription')}
          sectionName={t('detail.sections.course.name')}
          form={course}
          values={values}
          onSave={(submitted) => save('course', course, submitted)}
        >
          <CourseFields form={course} />
        </EditSheet>
      }
    >
      <DescriptionList
        items={[
          {
            term: t('form.trainingCompletedOn'),
            value: arquebusier.trainingCompletedOn
              ? t('detail.values.courseDone', { date: formatDate(arquebusier.trainingCompletedOn) })
              : t('detail.values.courseNotDone'),
          },
        ]}
      />
    </SectionCard>
  );
}

/** The read-only sections of the record, each with its own edit panel (spec: Registry screens). */
export function ArquebusierSections({ arquebusier, reload, announce, canWrite }: SectionsProps) {
  const { date } = useFormatters();
  const save = useSaveSection({ arquebusier, reload });
  const refresh = useRefreshArquebusier(arquebusier.id);
  const values = useMemo(() => valuesOf(arquebusier), [arquebusier]);
  const formatDate = (iso: string | null) => (iso ? date(new Date(`${iso}T12:00:00Z`), DATE) : '');
  const props = { arquebusier, values, save, formatDate, canWrite };
  return (
    <SectionGrid>
      <PersonalSection {...props} />
      <LicenseSection {...props} refresh={refresh} />
      <CourseSection {...props} />
      <OwnedWeaponsSection
        arquebusier={arquebusier}
        announce={announce}
        onChanged={refresh}
        canWrite={canWrite}
      />
    </SectionGrid>
  );
}
