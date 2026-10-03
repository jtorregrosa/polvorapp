import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { useWatch, type UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import {
  getGetComparsaOrderQueryKey,
  useUpdateEditionEntry,
  type getComparsaOrderResponse,
} from '@/api/generated/comparsa-orders/comparsa-orders';
import {
  ArquebusierStatus,
  CapsType,
  FlaskOption,
  WeaponSource,
  type EntryResponse,
  type OrderResponse,
} from '@/api/generated/model';
import { EditSheet, type EditResult } from '@/components/app/EditSheet';
import { FormField } from '@/components/app/FormField';
import { RadioCards, type RadioCardOption } from '@/components/app/RadioCards';
import { SelectInput } from '@/components/app/SelectInput';
import { TextInput } from '@/components/app/TextInput';
import { useAppForm } from '@/components/app/use-app-form';
import {
  ENTRY_FIELDS,
  entryContextOf,
  entrySchema,
  entryValuesOf,
  MAX_POWDER_KG,
  reserveValues,
  toEntryRequest,
  wholeNumber,
  type EntryValues,
} from '../entrySchema';
import { personName, useEntryText } from '../orderText';
import { applyFieldErrors, needsReload, problemCode, problemMessage } from '../problems';
import { orderChanged, reloadOrder } from '../queries';
import { AlertBanner } from '@/components/app/AlertBanner';
import { LoanFields, type LenderLookup } from './LoanFields';

export interface EntryEditSheetProps {
  order: OrderResponse;
  entry: EntryResponse;
  /**
   * The order can no longer be edited (orders closed, order validated or gone): the page reloads
   * it, which removes this panel, and shows `reason`.
   */
  onClosedOut: (reason: string) => void;
}

/** The refusals after which the order on screen can no longer be edited at all. */
const CLOSING_CODES = ['orders.closed', 'orders.validated', 'orders.notFound', 'entries.notFound'];

/** Sets every value of `next` on the form, as if the person had chosen them. */
function setAll(form: UseFormReturn<EntryValues>, next: EntryValues) {
  for (const [field, value] of Object.entries(next) as [keyof EntryValues, string][]) {
    form.setValue(field, value, { shouldDirty: true, shouldValidate: form.formState.isSubmitted });
  }
}

/** The weapon the entry takes: the arquebusier's own, a rental model, a loan or none (BR-07, BR-09). */
/** The fields of each weapon source: an error on one must not outlive its source. */
const SOURCE_FIELDS = [
  'ownedWeaponId',
  'rentalWeaponModelId',
  'loanMode',
  'loanOwnedWeaponId',
  'lenderNationalId',
  'lenderFirstName',
  'lenderLastName',
  'lenderWeaponModelId',
  'lenderWeaponNumber',
  'lenderOwnershipGuideNumber',
] as const;

function WeaponField({
  form,
  order,
  entry,
  disabled,
  lookup,
  onLookup,
}: {
  form: UseFormReturn<EntryValues>;
  order: OrderResponse;
  entry: EntryResponse;
  disabled: boolean;
  lookup: LenderLookup | null;
  onLookup: (lookup: LenderLookup | null) => void;
}) {
  const { t } = useTranslation('orders');
  const text = useEntryText();
  const ownedOptions = entry.ownedWeapons.flatMap((weapon) =>
    weapon.id === null
      ? []
      : [{ value: weapon.id, label: [weapon.modelLabel, weapon.weaponNumber].filter(Boolean).join(' ') }],
  );
  const keepsRemoved = entry.weaponSource === WeaponSource.OWNED && entry.ownedWeapon?.removed === true;
  const options: RadioCardOption[] = [
    ...(ownedOptions.length > 0 || keepsRemoved
      ? [
          {
            value: WeaponSource.OWNED,
            label: t('entryPanel.source.OWNED'),
            hint: keepsRemoved ? text.weapon(entry) : undefined,
            reveal:
              ownedOptions.length > 0 ? (
                <FormField
                  control={form.control}
                  name="ownedWeaponId"
                  label={t('entryPanel.ownedWeapon')}
                  optional={keepsRemoved}
                  width="name"
                >
                  {(field) => (
                    <SelectInput
                      {...field}
                      options={ownedOptions}
                      placeholder={t('entryPanel.choose')}
                      disabled={disabled}
                    />
                  )}
                </FormField>
              ) : undefined,
          },
        ]
      : []),
    ...(order.offeredModels.length > 0
      ? [
          {
            value: WeaponSource.RENTAL,
            label: t('entryPanel.source.RENTAL'),
            reveal: (
              <FormField
                control={form.control}
                name="rentalWeaponModelId"
                label={t('entryPanel.rentalModel')}
                width="name"
              >
                {(field) => (
                  <SelectInput
                    {...field}
                    options={order.offeredModels.map((model) => ({ value: model.id, label: model.label }))}
                    placeholder={t('entryPanel.choose')}
                    disabled={disabled}
                  />
                )}
              </FormField>
            ),
          },
        ]
      : []),
    {
      value: WeaponSource.LOAN,
      label: t('entryPanel.source.LOAN'),
      hint: t('entryPanel.source.LOAN_HINT'),
      reveal: <LoanFields form={form} entry={entry} lookup={lookup} onLookup={onLookup} />,
    },
    { value: WeaponSource.NONE, label: t('entryPanel.source.NONE'), hint: t('entryPanel.source.NONE_HINT') },
  ];
  return (
    <FormField control={form.control} name="weaponSource" label={t('entryPanel.weapon')}>
      {(field) => (
        <RadioCards
          {...field}
          options={options}
          disabled={disabled}
          onChange={(next) => {
            form.clearErrors([...SOURCE_FIELDS]);
            field.onChange(next);
          }}
        />
      )}
    </FormField>
  );
}

/**
 * Edits one entry of an order in a side panel, a bottom sheet on phones (spec: Orders screens):
 * status, powder, caps, weapon and flask, with the server's blocking rules checked before saving.
 * Choosing `RESERVE` clears and disables the rest (BR-05). Saving a submitted order's entry sends it
 * back to draft, and the notice says so.
 */
export function EntryEditSheet({ order, entry, onClosedOut }: EntryEditSheetProps) {
  const { t } = useTranslation('orders');
  const queryClient = useQueryClient();
  const context = useMemo(() => entryContextOf(order, entry), [order, entry]);
  const schema = useMemo(() => entrySchema(context), [context]);
  const values = useMemo(() => entryValuesOf(entry), [entry]);
  const form = useAppForm<EntryValues>({ resolver: zodResolver(schema), defaultValues: values });
  // Personal data of lenders: not kept in the mutation cache after the save settles.
  const update = useUpdateEditionEntry({ mutation: { gcTime: 0 } });
  const [lookup, setLookup] = useState<LenderLookup | null>(null);
  const status = useWatch({ control: form.control, name: 'status' });
  const capsBoxes = useWatch({ control: form.control, name: 'capsBoxes' });
  const reserve = status === ArquebusierStatus.RESERVE;
  const name = personName(entry.arquebusier);

  const onSave = async (submitted: EntryValues): Promise<EditResult> => {
    try {
      const response = await update.mutateAsync({
        id: order.id,
        entryId: entry.id,
        data: toEntryRequest(submitted),
      });
      const next = response.data as OrderResponse;
      await orderChanged(queryClient, next);
      return {
        status: 'saved',
        notice:
          order.status === 'SUBMITTED' && next.status === 'DRAFT'
            ? t('entryPanel.savedBackToDraft')
            : undefined,
      };
    } catch (error) {
      if (applyFieldErrors(error, ENTRY_FIELDS, form.setError)) return { status: 'kept' };
      const code = problemCode(error);
      if (code !== undefined && CLOSING_CODES.includes(code)) {
        // The reload removes this panel; the page then says why.
        await reloadOrder(queryClient, order.id);
        onClosedOut(problemMessage(t, error));
        return { status: 'kept' };
      }
      if (needsReload(error)) {
        await reloadOrder(queryClient, order.id);
        const fresh = queryClient.getQueryData<getComparsaOrderResponse>(
          getGetComparsaOrderQueryKey(order.id),
        )?.data as OrderResponse | undefined;
        const freshEntry = fresh?.entries.find((candidate) => candidate.id === entry.id);
        if (freshEntry) form.reset(entryValuesOf(freshEntry));
        return { status: 'conflict', reason: problemMessage(t, error) };
      }
      return { status: 'rejected', reason: problemMessage(t, error) };
    }
  };

  return (
    <EditSheet
      title={t('entryPanel.title', { name })}
      description={order.status === 'SUBMITTED' ? t('entryPanel.submittedNote') : undefined}
      sectionName={t('entries.sectionName', { name })}
      form={form}
      values={values}
      onSave={onSave}
    >
      <FormField control={form.control} name="status" label={t('entryPanel.status')}>
        {(field) => (
          <RadioCards
            {...field}
            options={[
              { value: ArquebusierStatus.ACTIVE, label: t('entryPanel.statusOption.ACTIVE') },
              {
                value: ArquebusierStatus.RESERVE,
                label: t('entryPanel.statusOption.RESERVE'),
                hint: t('entryPanel.reserveHint'),
              },
            ]}
            onChange={(next) => {
              if (next === ArquebusierStatus.RESERVE) {
                setAll(form, reserveValues(form.getValues()));
                form.clearErrors();
              } else field.onChange(next);
            }}
          />
        )}
      </FormField>
      {reserve && (
        <AlertBanner severity="info" live={false}>
          {t('entryPanel.reserveNote')}
        </AlertBanner>
      )}
      <FormField control={form.control} name="powderKg" label={t('entryPanel.powder')}>
        {(field) => (
          <RadioCards
            {...field}
            disabled={reserve}
            options={Array.from({ length: MAX_POWDER_KG + 1 }, (_, kg) => ({
              value: String(kg),
              label: t('totals.kg', { value: kg }),
            }))}
          />
        )}
      </FormField>
      <FormField control={form.control} name="capsBoxes" label={t('entryPanel.capsBoxes')} width="short">
        {(field) => (
          <TextInput
            {...field}
            inputMode="numeric"
            autoComplete="off"
            disabled={reserve}
            onChange={(event) => {
              field.onChange(event);
              if ((wholeNumber(event.target.value) ?? 0) === 0) form.clearErrors('capsType');
            }}
          />
        )}
      </FormField>
      {(wholeNumber(capsBoxes) ?? 0) > 0 && !reserve && (
        <FormField control={form.control} name="capsType" label={t('entryPanel.capsType')} width="name">
          {(field) => (
            <SelectInput
              {...field}
              placeholder={t('entryPanel.choose')}
              options={Object.values(CapsType).map((type) => ({
                value: type,
                label: t(`entryPanel.capsTypeOption.${type}`),
              }))}
            />
          )}
        </FormField>
      )}
      <WeaponField
        form={form}
        order={order}
        entry={entry}
        disabled={reserve}
        lookup={lookup}
        onLookup={setLookup}
      />
      <FormField control={form.control} name="flask" label={t('entryPanel.flask')}>
        {(field) => (
          <RadioCards
            {...field}
            disabled={reserve}
            options={Object.values(FlaskOption).map((flask) => ({
              value: flask,
              label: t(`entry.flask.${flask}`),
            }))}
          />
        )}
      </FormField>
    </EditSheet>
  );
}
