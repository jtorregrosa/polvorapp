import { zodResolver } from '@hookform/resolvers/zod';
import { Download, Search, Trash2, X } from 'lucide-react';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import type { ErasureResponse, PersonLookupResponse } from '@/api/generated/model';
import { getExportPersonDataUrl, useErasePersonData, useLookUpPerson } from '@/api/generated/privacy/privacy';
import { responseData } from '@/api/http';
import { AlertBanner, NoticeBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { Form, FormField } from '@/components/app/FormField';
import { PageHeader } from '@/components/app/PageHeader';
import { SectionCard } from '@/components/app/SectionCard';
import { SectionGrid } from '@/components/app/SectionGrid';
import { TextInput } from '@/components/app/TextInput';
import { useAppForm } from '@/components/app/use-app-form';
import { parseNationalId } from '@/features/arquebusier-registry/nationalId';
import { useFormatters } from '@/lib/format';
import { useNotice } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { PersonSummary } from '../components/PersonSummary';
import { PrivacyRequestDialog } from '../components/PrivacyRequestDialog';
import { erasureOutcome, forgetAuditPages } from '../erasure-outcome';
import { downloadPersonalData } from '../privacy-download';
import { privacyFieldError, privacyProblemMessage } from '../privacy-problems';

/** The DNI/NIE checked as the API does (BR-01), so a typo is caught before it is sent. */
const lookupSchema = z.object({
  nationalId: z.string().transform((value, context) => {
    const parsed = parseNationalId(value);
    if ('error' in parsed) {
      context.addIssue({ code: 'custom', message: `privacy:fields.nationalId.${parsed.error}` });
      return z.NEVER;
    }
    return parsed.value;
  }),
});

type LookupInput = z.input<typeof lookupSchema>;
type LookupValues = z.output<typeof lookupSchema>;

/** The person found, kept in memory only: the DNI/NIE never goes into the address (NFR-12). */
interface Found {
  nationalId: string;
  person: PersonLookupResponse;
}

/** Spec audit-privacy "GDPR request screens" (UC-26; design D12): look up, download and erase a person's data. */
export function PrivacyRequestsPage() {
  const { t } = useTranslation('privacy');
  useDocumentTitle(t('page.title'));
  const format = useFormatters();
  const [notice, announce, clearNotice] = useNotice();
  const [found, setFound] = useState<Found>();
  const lookUp = useLookUpPerson();
  const erase = useErasePersonData();
  const queryClient = useQueryClient();
  const summary = useRef<HTMLElement>(null);
  const nationalIdInput = useRef<HTMLInputElement | null>(null);
  const form = useAppForm<LookupInput, unknown, LookupValues>({
    resolver: zodResolver(lookupSchema),
    defaultValues: { nationalId: '' },
  });

  const submit = async ({ nationalId }: LookupValues): Promise<void> => {
    clearNotice();
    setFound(undefined);
    try {
      const person = responseData(await lookUp.mutateAsync({ data: { nationalId } }));
      setFound({ nationalId, person });
    } catch (error) {
      const fieldError = privacyFieldError(error, 'nationalId');
      if (fieldError) form.setError('nationalId', { type: 'server', message: fieldError });
    }
  };

  // The result is announced by moving focus to it (WCAG 4.1.3); "nothing held" focuses itself.
  useEffect(() => {
    if (found?.person.found) summary.current?.focus();
  }, [found]);

  /** Forgets the person: the result, the field and the requests that held their DNI/NIE. */
  const clear = (): void => {
    setFound(undefined);
    form.reset({ nationalId: '' });
    lookUp.reset();
    erase.reset();
  };

  const lookupFailed = lookUp.isError && !privacyFieldError(lookUp.error, 'nationalId');
  return (
    <>
      <PageHeader title={t('page.title')} description={t('page.description')} />
      <NoticeBanner notice={notice} />
      <SectionGrid>
        <SectionCard title={t('lookup.title')} description={t('lookup.description')} span="full">
          <Form form={form} onSubmit={submit} requiredNote={false}>
            <FormField control={form.control} name="nationalId" label={t('lookup.nationalId')} width="id">
              {(field) => (
                <TextInput
                  {...field}
                  ref={(node) => {
                    field.ref(node);
                    nationalIdInput.current = node;
                  }}
                  autoComplete="off"
                  spellCheck={false}
                  onChange={(event) => {
                    field.onChange(event);
                    // A result left under another DNI/NIE would offer that person's actions.
                    setFound(undefined);
                  }}
                />
              )}
            </FormField>
            <div className="flex flex-wrap gap-2">
              <Button type="submit" icon={Search} pending={lookUp.isPending}>
                {t('lookup.submit')}
              </Button>
              {found && (
                <Button
                  type="button"
                  variant="secondary"
                  icon={X}
                  onClick={() => {
                    clear();
                    // This button goes with the result: focus goes back to the field (WCAG 2.4.3).
                    nationalIdInput.current?.focus();
                  }}
                >
                  {t('lookup.clear')}
                </Button>
              )}
            </div>
          </Form>
          {lookupFailed && (
            <AlertBanner severity="error">{privacyProblemMessage(t, lookUp.error)}</AlertBanner>
          )}
        </SectionCard>
        {found && !found.person.found && (
          <AlertBanner severity="info" className="col-span-full" focusOnMount>
            {t('summary.nothingHeld')}
          </AlertBanner>
        )}
        {found?.person.found && (
          <PersonSummary
            ref={summary}
            person={found.person}
            actions={
              <div className="flex flex-wrap gap-2">
                <PrivacyRequestDialog
                  tone="primary"
                  title={t('export.title')}
                  description={t('export.description')}
                  confirmLabel={t('export.confirm')}
                  trigger={
                    <Button variant="secondary" icon={Download}>
                      {t('export.action')}
                    </Button>
                  }
                  onRequest={(reference) =>
                    downloadPersonalData(getExportPersonDataUrl(), {
                      nationalId: found.nationalId,
                      reference,
                    })
                  }
                  onDone={() => {
                    announce('success', t('export.done'));
                  }}
                />
                <ErasureDialog
                  found={found}
                  onRequest={async (reference) =>
                    responseData(
                      await erase.mutateAsync({ data: { nationalId: found.nationalId, reference } }),
                    )
                  }
                  onDone={(result) => {
                    clear();
                    forgetAuditPages(queryClient);
                    announce('success', erasureOutcome(t, format.list, result));
                  }}
                />
              </div>
            }
          />
        )}
      </SectionGrid>
    </>
  );
}

/** "Erase data": names the person, lists each warning before the reference, and repeats the verb. */
function ErasureDialog({
  found,
  onRequest,
  onDone,
}: {
  found: Found;
  onRequest: (reference: string) => Promise<ErasureResponse>;
  onDone: (result: ErasureResponse) => void;
}) {
  const { t } = useTranslation('privacy');
  const { t: tUi } = useTranslation('ui');
  const registry = found.person.registry;
  const name = registry ? `${registry.firstName} ${registry.lastName}` : undefined;

  const notes = (
    <div className="grid gap-2">
      <p className="text-label text-foreground">{t('erase.warnings.title')}</p>
      <ul className="grid list-disc gap-1 pl-5">
        <li>{t('erase.warnings.irreversible')}</li>
        {found.person.warnings.map((warning, index) => (
          // Warnings have no id; their order is the API's and does not change while shown.
          <li key={index}>
            {t(
              warning.kind === 'entryRemoved' ? 'erase.warnings.entryRemoved' : 'erase.warnings.listsChange',
              {
                year: warning.editionYear,
                comparsa: warning.comparsaName ?? t('summary.registry.unknownComparsa'),
                status: tUi(`status.order.${warning.orderStatus}` as 'status.order.DRAFT', {
                  defaultValue: warning.orderStatus,
                }),
              },
            )}
          </li>
        ))}
      </ul>
    </div>
  );

  return (
    <PrivacyRequestDialog
      tone="destructive"
      title={name ? t('erase.title', { name }) : t('erase.titleUnknown')}
      description={t('erase.description')}
      confirmLabel={t('erase.confirm')}
      notes={notes}
      trigger={
        <Button variant="destructive" icon={Trash2}>
          {t('erase.action')}
        </Button>
      }
      onRequest={onRequest}
      onDone={onDone}
    />
  );
}
