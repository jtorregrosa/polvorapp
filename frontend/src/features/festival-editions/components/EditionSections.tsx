import { zodResolver } from '@hookform/resolvers/zod';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import type { EditionResponse } from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { DateInput } from '@/components/app/DateInput';
import { DescriptionList } from '@/components/app/DescriptionList';
import { EditSheet } from '@/components/app/EditSheet';
import { FormField } from '@/components/app/FormField';
import { MoneyInput } from '@/components/app/MoneyInput';
import { SectionCard } from '@/components/app/SectionCard';
import { useAppForm } from '@/components/app/use-app-form';
import { useFormatters } from '@/lib/format';
import { moneyInputText } from '@/lib/money';
import { useEditionDates } from '../dates';
import {
  datesSchema,
  PRICE_FIELDS,
  pricesSchema,
  type DatesInput,
  type DatesValues,
  type PricesInput,
  type PricesValues,
} from '../editionSchema';
import { useSaveEdition } from './useSaveEdition';

const DATE_FIELDS = ['festivalStartsOn', 'festivalEndsOn', 'ordersOpenOn', 'ordersCloseOn'] as const;

const PRICE_PATHS = PRICE_FIELDS.map((field) => `prices.${field}` as const);

/** The festival dates and the order window (spec: Festival editions (UC-10)). */
export function DatesSection({ edition, canEdit }: { edition: EditionResponse; canEdit: boolean }) {
  const { t } = useTranslation('editions');
  const dates = useEditionDates();
  const save = useSaveEdition<DatesValues>(edition);
  const started = edition.status !== 'DRAFT';
  const schema = useMemo(() => datesSchema(edition.year, started), [edition.year, started]);
  const values = useMemo<DatesValues>(
    () => ({
      festivalStartsOn: edition.festivalStartsOn,
      festivalEndsOn: edition.festivalEndsOn,
      ordersOpenOn: edition.ordersOpenOn ?? '',
      ordersCloseOn: edition.ordersCloseOn ?? '',
    }),
    [edition],
  );
  const form = useAppForm<DatesValues, unknown, DatesInput>({
    resolver: zodResolver(schema),
    defaultValues: values,
  });

  return (
    <SectionCard
      title={t('sections.dates.title')}
      action={
        canEdit && (
          <EditSheet
            title={t('sections.dates.edit')}
            sectionName={t('sections.dates.name')}
            form={form}
            values={values}
            onSave={(submitted) => save(submitted, DATE_FIELDS, form.setError)}
          >
            <FormField
              control={form.control}
              name="festivalStartsOn"
              label={t('sections.dates.festivalStartsOn')}
              width="short"
            >
              {(field) => <DateInput {...field} />}
            </FormField>
            <FormField
              control={form.control}
              name="festivalEndsOn"
              label={t('sections.dates.festivalEndsOn')}
              width="short"
            >
              {(field) => <DateInput {...field} />}
            </FormField>
            <FormField
              control={form.control}
              name="ordersOpenOn"
              label={t('sections.dates.ordersOpenOn')}
              description={t('sections.dates.windowHelp')}
              optional={!started}
              width="short"
            >
              {(field) => <DateInput {...field} clearable={!started} />}
            </FormField>
            <FormField
              control={form.control}
              name="ordersCloseOn"
              label={t('sections.dates.ordersCloseOn')}
              optional={!started}
              width="short"
            >
              {(field) => <DateInput {...field} clearable={!started} />}
            </FormField>
          </EditSheet>
        )
      }
    >
      <DescriptionList
        items={[
          { term: t('sections.dates.festivalStartsOn'), value: dates.day(edition.festivalStartsOn) },
          { term: t('sections.dates.festivalEndsOn'), value: dates.day(edition.festivalEndsOn) },
          {
            term: t('sections.dates.ordersOpenOn'),
            value: edition.ordersOpenOn ? dates.day(edition.ordersOpenOn) : null,
          },
          {
            term: t('sections.dates.ordersCloseOn'),
            value: edition.ordersCloseOn ? dates.day(edition.ordersCloseOn) : null,
          },
        ]}
      />
    </SectionCard>
  );
}

/** The four flat prices, as currency (spec: Edition prices). */
export function PricesSection({ edition, canEdit }: { edition: EditionResponse; canEdit: boolean }) {
  const { t, i18n } = useTranslation('editions');
  const { currency } = useFormatters();
  const save = useSaveEdition<PricesValues>(edition);
  const started = edition.status !== 'DRAFT';
  const schema = useMemo(() => pricesSchema(started), [started]);
  const language = i18n.resolvedLanguage ?? i18n.language;
  const values = useMemo<PricesValues>(
    () => ({
      prices: {
        powderPerKg: moneyInputText(edition.prices.powderPerKg, language),
        capsBox: moneyInputText(edition.prices.capsBox, language),
        weaponRental: moneyInputText(edition.prices.weaponRental, language),
        flaskRental: moneyInputText(edition.prices.flaskRental, language),
      },
    }),
    [edition.prices, language],
  );
  const form = useAppForm<PricesValues, unknown, PricesInput>({
    resolver: zodResolver(schema),
    defaultValues: values,
  });

  return (
    <SectionCard
      title={t('sections.prices.title')}
      action={
        canEdit && (
          <EditSheet
            title={t('sections.prices.edit')}
            sectionName={t('sections.prices.name')}
            form={form}
            values={values}
            onSave={(submitted) => save({ prices: submitted.prices }, PRICE_PATHS, form.setError)}
          >
            {started && (
              <AlertBanner severity="info" live={false}>
                {t('sections.prices.billingNote')}
              </AlertBanner>
            )}
            {PRICE_FIELDS.map((price) => (
              <FormField
                key={price}
                control={form.control}
                name={`prices.${price}`}
                label={t(`sections.prices.${price}`)}
                optional={!started}
                width="short"
              >
                {(field) => <MoneyInput {...field} />}
              </FormField>
            ))}
          </EditSheet>
        )
      }
    >
      <DescriptionList
        items={PRICE_FIELDS.map((price) => {
          const amount = edition.prices[price];
          return { term: t(`sections.prices.${price}`), value: amount === null ? null : currency(amount) };
        })}
      />
    </SectionCard>
  );
}
