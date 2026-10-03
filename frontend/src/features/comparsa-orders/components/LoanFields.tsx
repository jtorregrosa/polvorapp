import { Search } from 'lucide-react';
import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { useWatch, type UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useLookUpLender } from '@/api/generated/comparsa-orders/comparsa-orders';
import type {
  EntryResponse,
  LenderLookupResponse,
  LenderResponse,
  WeaponModelResponse,
} from '@/api/generated/model';
import { useListWeaponModels } from '@/api/generated/weapon-models/weapon-models';
import { ApiProblemError } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { FormField } from '@/components/app/FormField';
import { RadioCards } from '@/components/app/RadioCards';
import { SelectInput } from '@/components/app/SelectInput';
import { TextInput } from '@/components/app/TextInput';
import { parseNationalId } from '@/features/arquebusier-registry/nationalId';
import { LoanMode, type EntryValues } from '../entrySchema';
import { personName, useEntryText } from '../orderText';
import { messages, problemMessage } from '../problems';

const TOO_MANY_REQUESTS = 429;

/** The outcome of the last lookup, for the DNI/NIE it was made with; kept by the panel. */
export interface LenderLookup {
  nationalId: string;
  lender: LenderResponse | null;
}

/** The lookup fields that a new DNI/NIE makes out of date. */
const LOOKUP_FIELDS = [
  'loanOwnedWeaponId',
  'lenderFirstName',
  'lenderLastName',
  'lenderWeaponModelId',
  'lenderWeaponNumber',
  'lenderOwnershipGuideNumber',
] as const;

/**
 * The lender of a `LOAN` entry (spec: Weapon loans (UC-13, BR-09), Lender lookup (UC-13, BR-12)):
 * the current loan is kept until the person changes it; then they type the owner's DNI/NIE, which
 * is checked first and sent in a request body, never in the address. A registered owner's weapons
 * are offered; otherwise the external owner and their weapon are typed in. The lookup belongs to
 * the DNI/NIE it was made with: editing it forgets the result. The outcome is announced in a polite
 * live region that is always present.
 */
export function LoanFields({
  form,
  entry,
  lookup,
  onLookup,
}: {
  form: UseFormReturn<EntryValues>;
  entry: EntryResponse;
  /** Kept by the panel, so choosing another weapon source and coming back keeps it. */
  lookup: LenderLookup | null;
  onLookup: (lookup: LenderLookup | null) => void;
}) {
  const { t } = useTranslation('orders');
  const text = useEntryText();
  const mode = useWatch({ control: form.control, name: 'loanMode' });
  const lookUp = useLookUpLender({ mutation: { gcTime: 0 } });
  const [failure, setFailure] = useState<string>();
  const [status, setStatus] = useState('');
  const focusDni = useRef(false);
  const models = useListWeaponModels(undefined, { query: { enabled: mode === LoanMode.EXTERNAL } });
  const modelOptions = ((models.data?.data ?? []) as WeaponModelResponse[])
    .filter((model) => model.active)
    .map((model) => ({ value: model.id, label: model.label }));

  // After "Change the owner", the button is gone: focus moves to the DNI/NIE it asks for.
  useEffect(() => {
    if (focusDni.current && mode !== LoanMode.KEEP) {
      focusDni.current = false;
      form.setFocus('lenderNationalId');
    }
  }, [mode, form]);

  const forget = () => {
    if (!lookup && mode !== LoanMode.REGISTERED && mode !== LoanMode.EXTERNAL) return;
    onLookup(null);
    form.setValue('loanMode', '');
    for (const field of LOOKUP_FIELDS) form.setValue(field, '');
    form.clearErrors([...LOOKUP_FIELDS, 'loanMode']);
  };

  const search = () => {
    setFailure(undefined);
    const parsed = parseNationalId(form.getValues('lenderNationalId'));
    if ('error' in parsed) {
      form.setError(
        'lenderNationalId',
        {
          type: 'client',
          message:
            parsed.error === 'required'
              ? messages.required
              : parsed.error === 'checkLetter'
                ? messages.checkLetter
                : messages.invalid,
        },
        { shouldFocus: true },
      );
      return;
    }
    form.clearErrors('lenderNationalId');
    setStatus(t('loan.searching'));
    lookUp.mutate(
      { data: { nationalId: parsed.value } },
      {
        onSuccess: (response) => {
          // A DNI/NIE edited while the lookup ran makes its answer out of date.
          const current = parseNationalId(form.getValues('lenderNationalId'));
          if (!('value' in current) || current.value !== parsed.value) {
            setStatus('');
            return;
          }
          const result = response.data as LenderLookupResponse;
          const lender = result.registered ? result.lender : null;
          onLookup({ nationalId: parsed.value, lender });
          for (const field of LOOKUP_FIELDS) form.setValue(field, '');
          form.setValue('loanMode', lender ? LoanMode.REGISTERED : LoanMode.EXTERNAL, { shouldDirty: true });
          form.clearErrors([...LOOKUP_FIELDS, 'loanMode']);
          setStatus(
            lender
              ? t('loan.found', { name: personName(lender), comparsa: lender.comparsaName })
              : t('loan.notFound'),
          );
        },
        onError: (error) => {
          setStatus('');
          setFailure(
            error instanceof ApiProblemError && error.status === TOO_MANY_REQUESTS
              ? t('loan.tooManyLookups')
              : problemMessage(t, error),
          );
        },
      },
    );
  };

  if (mode === LoanMode.KEEP && entry.loan) {
    return (
      <div className="flex flex-col items-start gap-2">
        <p>{text.weapon(entry)}</p>
        <Button
          type="button"
          size="sm"
          variant="secondary"
          onClick={() => {
            focusDni.current = true;
            form.setValue('loanMode', '', { shouldDirty: true });
          }}
        >
          {t('loan.change')}
        </Button>
      </div>
    );
  }

  const lender = mode === LoanMode.REGISTERED ? lookup?.lender : null;
  return (
    <div className="flex flex-col gap-group">
      <FormField
        control={form.control}
        name="lenderNationalId"
        label={t('loan.nationalId')}
        description={t('loan.nationalIdHint')}
        width="id"
      >
        {(field) => (
          <TextInput
            {...field}
            autoComplete="off"
            spellCheck={false}
            onChange={(event) => {
              field.onChange(event);
              setFailure(undefined);
              setStatus('');
              form.clearErrors('lenderNationalId');
              forget();
            }}
            onKeyDown={(event: KeyboardEvent<HTMLInputElement>) => {
              // Enter looks the owner up instead of saving the whole entry.
              if (event.key === 'Enter') {
                event.preventDefault();
                search();
              }
            }}
          />
        )}
      </FormField>
      <div>
        <Button
          type="button"
          size="sm"
          variant="secondary"
          icon={Search}
          pending={lookUp.isPending}
          onClick={search}
        >
          {t('loan.lookUp')}
        </Button>
      </div>
      <p role="status" className="text-help">
        {status}
      </p>
      {failure && <AlertBanner severity="error">{failure}</AlertBanner>}
      {lender &&
        (lender.weapons.length === 0 ? (
          <p>{t('loan.noWeapons')}</p>
        ) : (
          <FormField control={form.control} name="loanOwnedWeaponId" label={t('loan.weapon')}>
            {(field) => (
              <RadioCards
                {...field}
                options={lender.weapons.map((weapon) => ({
                  value: weapon.id,
                  label: `${weapon.weaponModel.label} ${weapon.weaponNumber}`,
                }))}
              />
            )}
          </FormField>
        ))}
      {mode === LoanMode.EXTERNAL && (
        <>
          <FormField control={form.control} name="lenderFirstName" label={t('loan.firstName')} width="name">
            {(field) => <TextInput {...field} autoComplete="off" />}
          </FormField>
          <FormField control={form.control} name="lenderLastName" label={t('loan.lastName')} width="name">
            {(field) => <TextInput {...field} autoComplete="off" />}
          </FormField>
          {models.isError && (
            <AlertBanner severity="error">
              <span className="flex flex-col items-start gap-2">
                {t('loan.modelsFailed')}
                <Button
                  type="button"
                  size="sm"
                  variant="secondary"
                  onClick={() => {
                    void models.refetch();
                  }}
                >
                  {t('loan.retry')}
                </Button>
              </span>
            </AlertBanner>
          )}
          <FormField control={form.control} name="lenderWeaponModelId" label={t('loan.model')} width="name">
            {(field) => (
              <SelectInput {...field} options={modelOptions} placeholder={t('entryPanel.choose')} />
            )}
          </FormField>
          <FormField
            control={form.control}
            name="lenderWeaponNumber"
            label={t('loan.weaponNumber')}
            width="id"
          >
            {(field) => <TextInput {...field} autoComplete="off" />}
          </FormField>
          <FormField
            control={form.control}
            name="lenderOwnershipGuideNumber"
            label={t('loan.guideNumber')}
            width="id"
          >
            {(field) => <TextInput {...field} autoComplete="off" />}
          </FormField>
        </>
      )}
    </div>
  );
}
