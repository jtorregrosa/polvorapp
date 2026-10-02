import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useAppForm } from '@/components/app/use-app-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router';
import {
  getGetArquebusierQueryKey,
  getListArquebusiersQueryKey,
  uploadArquebusierPhoto,
  useRegisterArquebusier,
  type registerArquebusierResponse,
} from '@/api/generated/arquebusiers/arquebusiers';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import type { ArquebusierResponse, ComparsaResponse } from '@/api/generated/model';
import { responseData } from '@/api/http';
import { ActionBar } from '@/components/app/ActionBar';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { Form } from '@/components/app/FormField';
import { FormLayout } from '@/components/app/FormLayout';
import { PageHeader } from '@/components/app/PageHeader';
import { PhotoUpload } from '@/components/app/PhotoUpload';
import { useSession } from '@/features/identity-access/session';
import { noticeState } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { invalidateInsights } from '@/features/compliance-insights/queries';
import {
  ARQUEBUSIER_FIELDS,
  EMPTY_ARQUEBUSIER,
  registerSchema,
  requestFields,
  type ArquebusierValues,
} from '../arquebusierSchema';
import { LoadFailure } from '../components/LoadFailure';
import { ID_PHOTO_RULES } from '../photos';
import { applyFieldErrors, photoProblemMessage, problemMessage } from '../problems';
import { CourseFields, LicenseFields, PersonalFields, RegisterFields } from './ArquebusierFields';

/** Spec "Registering and editing arquebusiers": registers an arquebusier in an active comparsa in scope. */
export function ArquebusierFormPage() {
  const { t } = useTranslation('registry');
  useDocumentTitle(t('form.newTitle'));
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const session = useSession();
  const isAdmin = session.account?.role === 'ADMIN';
  const register = useRegisterArquebusier();
  // Without includeInactive the API lists only active comparsas, within the caller's scope.
  const comparsas = useListComparsas(undefined, { query: { enabled: session.status === 'signedIn' } });
  const active = useMemo(
    () => ((comparsas.data?.data ?? []) as ComparsaResponse[]).filter((comparsa) => comparsa.active),
    [comparsas.data],
  );
  const noComparsa = comparsas.isSuccess && active.length === 0;
  const form = useAppForm<ArquebusierValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: EMPTY_ARQUEBUSIER,
  });
  // The ID photo is chosen and cropped now, and uploaded once the arquebusier exists.
  const [idPhoto, setIdPhoto] = useState<{ blob: Blob; url: string }>();
  const idPhotoUrl = useRef<string>(undefined);
  useEffect(() => {
    return () => {
      if (idPhotoUrl.current) URL.revokeObjectURL(idPhotoUrl.current);
    };
  }, []);
  const chooseIdPhoto = (blob: Blob) => {
    if (idPhotoUrl.current) URL.revokeObjectURL(idPhotoUrl.current);
    const url = URL.createObjectURL(blob);
    idPhotoUrl.current = url;
    setIdPhoto({ blob, url });
  };

  // Spec "Registry screens": the only comparsa in scope is pre-selected.
  useEffect(() => {
    if (active.length === 1 && form.getValues('comparsaId') === '') {
      form.setValue('comparsaId', active[0]?.id ?? '');
    }
  }, [active, form]);

  const onSubmit = async (values: ArquebusierValues): Promise<void> => {
    let response: registerArquebusierResponse;
    try {
      response = await register.mutateAsync({
        data: { comparsaId: values.comparsaId, ...requestFields(values) },
      });
    } catch (error) {
      const placed = applyFieldErrors(error, ARQUEBUSIER_FIELDS, form.setError, {
        isAdmin,
        conflicts: {
          'arquebusiers.nationalIdTaken': 'nationalId',
          'arquebusiers.federationIdTaken': 'federationId',
          'arquebusiers.comparsaNotFound': 'comparsaId',
          'arquebusiers.comparsaInactive': 'comparsaId',
        },
      });
      // A refusal about no field is listed in the error summary, which takes focus.
      if (!placed)
        form.setError('root.server', { type: 'server', message: problemMessage(t, error, { isAdmin }) });
      return;
    }
    let created: ArquebusierResponse;
    try {
      created = responseData(response);
    } catch {
      void queryClient.invalidateQueries({ queryKey: getListArquebusiersQueryKey() });
      void invalidateInsights(queryClient);
      // Registered, but the answer had no body: show the list, where the new arquebusier is. A chosen
      // photo could not be uploaded without the new id, so say so.
      await navigate('/arquebusiers', {
        state: idPhoto
          ? noticeState(t('form.registeredUnnamedPhotoNotSaved'), 'error')
          : noticeState(t('form.registeredUnnamed')),
      });
      return;
    }
    const name = `${created.firstName} ${created.lastName}`;
    // After the photo upload, so the list never caches the new row without its photo.
    const refreshList = () => {
      void queryClient.invalidateQueries({ queryKey: getListArquebusiersQueryKey() });
      void invalidateInsights(queryClient);
    };
    if (idPhoto) {
      try {
        await uploadArquebusierPhoto(created.id, 'id', { file: idPhoto.blob });
      } catch (error) {
        // Spec "Photo screens": registered without the photo; the detail page says why and offers it again.
        refreshList();
        queryClient.setQueryData(getGetArquebusierQueryKey(created.id), { ...response, status: 200 });
        await navigate(`/arquebusiers/${created.id}`, {
          state: noticeState(
            t('photos.uploadFailedAfterRegister', { name, reason: photoProblemMessage(t, error) }),
            'error',
          ),
        });
        return;
      }
      // The detail page reads the new photo from the API.
      refreshList();
      await navigate(`/arquebusiers/${created.id}`, { state: noticeState(t('form.registered', { name })) });
      return;
    }
    // The detail page opens on the data just returned, so its notice is shown (and focused) at once.
    refreshList();
    queryClient.setQueryData(getGetArquebusierQueryKey(created.id), { ...response, status: 200 });
    await navigate(`/arquebusiers/${created.id}`, { state: noticeState(t('form.registered', { name })) });
  };

  const pending = register.isPending || form.formState.isSubmitting;
  const idPhotoControl = (
    <PhotoUpload
      label={t('photos.idPhoto')}
      photoUrl={idPhoto?.url ?? null}
      photoAlt={t('photos.chosenIdPhotoAlt')}
      emptyText={t('photos.noIdPhoto')}
      uploadedText={t('photos.chosenForRegistration')}
      // The registration sends the photo chosen when it started.
      disabled={pending}
      onUpload={(photo) => {
        chooseIdPhoto(photo);
        return Promise.resolve();
      }}
      {...ID_PHOTO_RULES}
    />
  );

  return (
    <>
      <PageHeader
        title={t('form.newTitle')}
        description={t('form.newDescription')}
        back={{ to: '/arquebusiers', label: t('detail.back') }}
      />
      {comparsas.isError && (
        <LoadFailure
          className="max-w-form"
          error={comparsas.error}
          consequence={t('load.comparsas')}
          onRetry={() => comparsas.refetch()}
        />
      )}
      {noComparsa && (
        <AlertBanner severity="info" className="max-w-form">
          {t('form.noActiveComparsa')}
        </AlertBanner>
      )}
      <Form form={form} onSubmit={onSubmit}>
        <FormLayout
          sections={[
            {
              id: 'comparsa',
              title: t('form.membership'),
              description: t('form.membershipDescription'),
              content: <RegisterFields form={form} comparsas={active} />,
            },
            {
              id: 'personal',
              title: t('form.personal'),
              description: t('form.personalDescription'),
              content: <PersonalFields form={form} idPhoto={idPhotoControl} />,
            },
            {
              id: 'license',
              title: t('form.license'),
              description: t('form.licenseDescription'),
              content: <LicenseFields form={form} />,
            },
            {
              id: 'training',
              title: t('form.training'),
              description: t('form.trainingDescription'),
              content: <CourseFields form={form} />,
            },
          ]}
          help={<p>{t('form.help')}</p>}
          actions={
            <ActionBar
              secondary={
                <Button asChild variant="secondary">
                  <Link to="/arquebusiers">{t('form.cancel')}</Link>
                </Button>
              }
              primary={
                // Pending until the ID photo is uploaded too, not only while the arquebusier is created.
                <Button type="submit" pending={pending} disabled={comparsas.isError || noComparsa}>
                  {t('form.register')}
                </Button>
              }
            />
          }
        />
      </Form>
    </>
  );
}
