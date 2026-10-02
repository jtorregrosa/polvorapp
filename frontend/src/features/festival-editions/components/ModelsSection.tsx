import { zodResolver } from '@hookform/resolvers/zod';
import { TriangleAlert } from 'lucide-react';
import { useMemo } from 'react';
import { useWatch, type UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useSetEditionWeaponModels } from '@/api/generated/editions/editions';
import { WeaponKind, type EditionResponse, type WeaponModelResponse } from '@/api/generated/model';
import { useListWeaponModels } from '@/api/generated/weapon-models/weapon-models';
import { CheckboxField } from '@/components/app/CheckboxField';
import { EditSheet, type EditResult } from '@/components/app/EditSheet';
import { SectionCard } from '@/components/app/SectionCard';
import { useAppForm } from '@/components/app/use-app-form';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { modelsSchema, type ModelsValues } from '../editionSchema';
import { problemCode, validationReason } from '../problems';
import { useEditionRefresh, useExplain } from './useSaveEdition';

const KINDS = Object.values(WeaponKind);

interface ModelChoice {
  id: string;
  label: string;
  kind: WeaponKind;
  /** In the set although no longer active and rentable: it can only be removed. */
  retired: boolean;
}

/**
 * The choices of the edit panel: the catalogue's active, rentable models, and any model already in
 * the set that is no longer rentable (spec: Rental models offered in an edition (BR-07)).
 */
function choicesOf(edition: EditionResponse, catalogue: readonly WeaponModelResponse[]): ModelChoice[] {
  const rentable = catalogue
    .filter((model) => model.active && model.rentable)
    .map((model) => ({ id: model.id, label: model.label, kind: model.kind, retired: false }));
  const retired = edition.weaponModels
    .filter((model) => !model.offered && !rentable.some((choice) => choice.id === model.id))
    .map((model) => ({ id: model.id, label: model.label, kind: model.kind, retired: true }));
  return [...rentable, ...retired];
}

function ModelChoices({
  form,
  choices,
  catalogue,
}: {
  form: UseFormReturn<ModelsValues>;
  choices: readonly ModelChoice[];
  /** The catalogue request: its models are the choices, so they are not offered before it loads. */
  catalogue: { isPending: boolean; isError: boolean; error: unknown; refetch: () => unknown };
}) {
  const { t } = useTranslation(['editions', 'catalog']);
  const selected = useWatch({ control: form.control, name: 'weaponModelIds' });
  if (catalogue.isError) {
    return (
      <LoadFailure
        error={catalogue.error}
        consequence={t('sections.models.loadFailed')}
        onRetry={catalogue.refetch}
      />
    );
  }
  if (catalogue.isPending) {
    return <p className="text-body text-muted-foreground">{t('sections.models.loading')}</p>;
  }
  if (choices.length === 0) {
    return <p className="text-body text-muted-foreground">{t('sections.models.noneAvailable')}</p>;
  }
  const toggle = (id: string, checked: boolean) => {
    const next = checked ? [...selected, id] : selected.filter((current) => current !== id);
    form.setValue('weaponModelIds', next, { shouldDirty: true });
  };
  return (
    <>
      <p className="text-help text-muted-foreground">{t('sections.models.help')}</p>
      {KINDS.map((kind) => {
        const ofKind = choices.filter((choice) => choice.kind === kind);
        if (ofKind.length === 0) return null;
        return (
          <fieldset key={kind} className="flex flex-col gap-3">
            <legend className="mb-2 text-label text-foreground">{t(`catalog:kind.${kind}`)}</legend>
            {ofKind.map((choice) => {
              const checked = selected.includes(choice.id);
              return (
                <CheckboxField
                  key={choice.id}
                  label={choice.label}
                  description={choice.retired ? t('sections.models.retiredHelp') : undefined}
                  checked={checked}
                  disabled={choice.retired && !checked}
                  onCheckedChange={(next) => {
                    toggle(choice.id, next);
                  }}
                />
              );
            })}
          </fieldset>
        );
      })}
    </>
  );
}

/** The rental models offered in the edition, the ones no longer rented marked (BR-07). */
export function ModelsSection({ edition, canEdit }: { edition: EditionResponse; canEdit: boolean }) {
  const { t } = useTranslation('editions');
  const catalogue = useListWeaponModels(undefined, { query: { enabled: canEdit } });
  const set = useSetEditionWeaponModels();
  const refresh = useEditionRefresh(edition.id);
  const explain = useExplain();
  const values = useMemo<ModelsValues>(
    () => ({ weaponModelIds: edition.weaponModels.map((model) => model.id) }),
    [edition],
  );
  const form = useAppForm<ModelsValues>({ resolver: zodResolver(modelsSchema), defaultValues: values });
  const choices = useMemo(
    () => choicesOf(edition, (catalogue.data?.data ?? []) as WeaponModelResponse[]),
    [edition, catalogue.data],
  );

  const save = async (submitted: ModelsValues): Promise<EditResult> => {
    try {
      await set.mutateAsync({ id: edition.id, data: submitted });
    } catch (error) {
      if (problemCode(error) === 'editions.notFound') {
        await refresh();
        return { status: 'conflict', reason: explain(error) };
      }
      // The set has no single field to point at: a refused model is the panel's reason, in words.
      const reason = validationReason(error, 'weaponModelIds');
      return { status: 'rejected', reason: reason ? t(`validation.${reason}`) : explain(error) };
    }
    await refresh();
    return { status: 'saved' };
  };

  return (
    <SectionCard
      title={t('sections.models.title')}
      action={
        canEdit && (
          <EditSheet
            title={t('sections.models.edit')}
            sectionName={t('sections.models.name')}
            form={form}
            values={values}
            onSave={save}
          >
            <ModelChoices form={form} choices={choices} catalogue={catalogue} />
          </EditSheet>
        )
      }
    >
      {edition.weaponModels.length === 0 ? (
        <p className="text-body text-muted-foreground">{t('sections.models.empty')}</p>
      ) : (
        <ul className="flex flex-col gap-1.5 text-body">
          {edition.weaponModels.map((model) => (
            <li key={model.id} className="flex flex-wrap items-baseline gap-x-2">
              <span>{model.label}</span>
              {!model.offered && (
                <span className="inline-flex items-center gap-1 text-help text-muted-foreground">
                  <TriangleAlert aria-hidden="true" className="size-3.5" />
                  {t('sections.models.notRentable')}
                </span>
              )}
            </li>
          ))}
        </ul>
      )}
    </SectionCard>
  );
}
