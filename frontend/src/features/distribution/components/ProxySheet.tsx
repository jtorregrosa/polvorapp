import { zodResolver } from '@hookform/resolvers/zod';
import { UserPlus } from 'lucide-react';
import { useCallback, useMemo, type Ref } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useListProxyCandidates, useRegisterPickupProxy } from '@/api/generated/distribution/distribution';
import type { ComparsaRef, DistributionType, ProxyCandidateResponse } from '@/api/generated/model';
import { EditSheet, type EditResult } from '@/components/app/EditSheet';
import { FormField } from '@/components/app/FormField';
import { RadioCards } from '@/components/app/RadioCards';
import { SelectInput, type SelectOption } from '@/components/app/SelectInput';
import { useAppForm } from '@/components/app/use-app-form';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { useFormatters } from '@/lib/format';
import { applyFieldErrors, isStale, problemCode, problemMessage, sameNames } from '../problems';
import { useDistributionRefresh } from '../queries';
import { proxySchema, type ProxyInput, type ProxyValues } from '../schemas';

type ProxyForm = UseFormReturn<ProxyValues, unknown, ProxyInput>;

const TYPES: readonly DistributionType[] = ['POWDER', 'WEAPONS'];

const FIELDS = sameNames<ProxyValues>('type', 'holderEntryId', 'proxyEntryId');

/** Conflicts about one person: shown on their field. */
const CONFLICTS = {
  'proxies.alreadyAuthorised': 'holderEntryId',
  'proxies.holderIsProxy': 'holderEntryId',
  'proxies.proxyAbsent': 'proxyEntryId',
} as const;

/** Why a person cannot be a proxy for `type`, in words; undefined when they can. */
function useIneligibleReason() {
  const { t } = useTranslation('distribution');
  return useCallback(
    (candidate: ProxyCandidateResponse, type: string) => {
      const restriction = candidate.cannotCollect.find((cannot) => cannot.type === type);
      if (!restriction) return undefined;
      if (restriction.reason === 'proxyAbsent') return t('proxies.ineligible.proxyAbsent');
      if (restriction.reason === 'licenseInvalid') return t('proxies.ineligible.licenseInvalid');
      return t('proxies.ineligible.other');
    },
    [t],
  );
}

/**
 * Who can be the holder and who the proxy for `type`. The people who cannot be the proxy stay in the
 * list, disabled, with the reason in their label; as browsers skip disabled options, the reasons are
 * also summed up in the field's description.
 */
function usePeople(candidates: readonly ProxyCandidateResponse[], type: string, holder: string) {
  const { t } = useTranslation('distribution');
  const { list } = useFormatters();
  const reasonOf = useIneligibleReason();
  return useMemo(() => {
    const holders: SelectOption[] = candidates
      .filter((candidate) => candidate.canBeHeldFor.some((held) => held === type))
      .map((candidate) => ({ value: candidate.entryId, label: candidate.name }));
    const others = candidates
      .filter((candidate) => candidate.entryId !== holder)
      .map((candidate) => {
        const reason = reasonOf(candidate, type);
        return {
          candidate,
          label: reason ? t('proxies.notEligible', { name: candidate.name, reason }) : undefined,
        };
      });
    const proxies: SelectOption[] = others.map(({ candidate, label }) =>
      label
        ? { value: candidate.entryId, label, disabled: true }
        : { value: candidate.entryId, label: candidate.name },
    );
    const ineligible = others.flatMap(({ label }) => (label ? [label] : []));
    const summary =
      ineligible.length > 0 ? t('proxies.notEligibleSummary', { people: list(ineligible) }) : '';
    return { holders, proxies, summary };
  }, [candidates, type, holder, t, list, reasonOf]);
}

function ProxyFields({
  form,
  editionId,
  comparsas,
}: {
  form: ProxyForm;
  editionId: string;
  comparsas: readonly ComparsaRef[];
}) {
  const { t } = useTranslation('distribution');
  const [comparsaId, type, holder] = form.watch(['comparsaId', 'type', 'holderEntryId']);
  const candidates = useListProxyCandidates(editionId, comparsaId, {
    query: { enabled: comparsaId !== '' },
  });
  const loaded = candidates.data?.data as ProxyCandidateResponse[] | undefined;
  const people = usePeople(loaded ?? [], type, holder);
  // A new comparsa or type changes who can be chosen: the people are chosen again.
  const resetPeople = () => {
    form.setValue('holderEntryId', '');
    form.setValue('proxyEntryId', '');
  };

  const peopleFields = () => {
    if (type === '' || comparsaId === '') return null;
    if (candidates.isError && !loaded) {
      return (
        <LoadFailure
          error={candidates.error}
          consequence={t('proxies.peopleFailed')}
          onRetry={() => candidates.refetch()}
        />
      );
    }
    if (!loaded) {
      return (
        <p role="status" className="text-help text-muted-foreground">
          {t('proxies.loadingPeople')}
        </p>
      );
    }
    if (people.holders.length === 0) {
      return (
        <p role="status" className="text-help text-muted-foreground">
          {t('proxies.noHolders')}
        </p>
      );
    }
    return (
      <>
        <FormField
          control={form.control}
          name="holderEntryId"
          label={t('proxies.holder')}
          description={t('proxies.holderHint')}
          width="long"
        >
          {(field) => (
            <SelectInput
              {...field}
              placeholder={t('proxies.chooseHolder')}
              options={people.holders}
              onChange={(event) => {
                field.onChange(event);
                form.setValue('proxyEntryId', '');
              }}
            />
          )}
        </FormField>
        <FormField
          control={form.control}
          name="proxyEntryId"
          label={t('proxies.proxy')}
          description={[t('proxies.proxyHint'), people.summary].filter(Boolean).join(' ')}
          width="long"
        >
          {(field) => (
            <SelectInput {...field} placeholder={t('proxies.chooseProxy')} options={people.proxies} />
          )}
        </FormField>
      </>
    );
  };

  return (
    <>
      {comparsas.length > 1 && (
        <FormField control={form.control} name="comparsaId" label={t('proxies.comparsa')} width="long">
          {(field) => (
            <SelectInput
              {...field}
              placeholder={t('proxies.chooseComparsa')}
              options={comparsas.map((comparsa) => ({ value: comparsa.id, label: comparsa.name }))}
              onChange={(event) => {
                field.onChange(event);
                resetPeople();
              }}
            />
          )}
        </FormField>
      )}
      <FormField control={form.control} name="type" label={t('proxies.type')}>
        {(field) => (
          <RadioCards
            {...field}
            options={TYPES.map((value) => ({
              value,
              label: t(`types.${value}`),
              hint: t(`proxies.typeHint.${value}`),
            }))}
            onChange={(next) => {
              field.onChange(next);
              resetPeople();
            }}
          />
        )}
      </FormField>
      {peopleFields()}
    </>
  );
}

interface ProxySheetProps {
  editionId: string;
  /** The comparsas the user may register proxies for; the choice is offered when there are several. */
  comparsas: readonly ComparsaRef[];
  /** "Add proxy", where focus goes once a proxy is removed. */
  triggerRef?: Ref<HTMLButtonElement>;
  /** Hides the button while proxies cannot be changed, keeping an open panel and its reason. */
  hideTrigger?: boolean;
}

/**
 * "Add proxy" (spec: Pickup proxies): the comparsa when there are several, the type, the holder
 * among those with something of that type to collect and no proxy for it, and the proxy among the
 * other entries of the order. The reason is not stored: it is written by hand on the form.
 */
export function ProxySheet({ editionId, comparsas, triggerRef, hideTrigger }: ProxySheetProps) {
  const { t } = useTranslation('distribution');
  const register = useRegisterPickupProxy();
  const refresh = useDistributionRefresh(editionId);
  const only = comparsas.length === 1 ? comparsas[0] : undefined;
  const values = useMemo<ProxyValues>(
    () => ({ comparsaId: only?.id ?? '', type: '', holderEntryId: '', proxyEntryId: '' }),
    [only],
  );
  const form = useAppForm<ProxyValues, unknown, ProxyInput>({
    resolver: zodResolver(proxySchema),
    defaultValues: values,
  });

  const onSave = async ({ type, holderEntryId, proxyEntryId }: ProxyInput): Promise<EditResult> => {
    try {
      await register.mutateAsync({ editionId, data: { type, holderEntryId, proxyEntryId } });
    } catch (error) {
      // Someone else changed the proxies or the edition: the proxies and candidates are read again.
      if (isStale(error)) await refresh();
      if (problemCode(error) === 'distribution.editionNotInProgress') {
        return { status: 'conflict', reason: problemMessage(t, error) };
      }
      return applyFieldErrors(error, FIELDS, form.setError, CONFLICTS)
        ? { status: 'kept' }
        : { status: 'rejected', reason: problemMessage(t, error) };
    }
    await refresh();
    return { status: 'saved' };
  };

  return (
    <EditSheet
      title={t('proxies.addTitle')}
      description={t('proxies.reasonNote')}
      sectionName={t('proxies.title')}
      trigger={{ label: t('proxies.add'), icon: UserPlus }}
      triggerRef={triggerRef}
      hideTrigger={hideTrigger}
      form={form}
      values={values}
      savedText={t('proxies.added')}
      onSave={onSave}
    >
      <ProxyFields form={form} editionId={editionId} comparsas={comparsas} />
    </EditSheet>
  );
}
