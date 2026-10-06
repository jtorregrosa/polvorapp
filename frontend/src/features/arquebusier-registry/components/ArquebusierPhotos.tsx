import { useId } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useRemoveArquebusierPhoto,
  useUploadArquebusierPhoto,
} from '@/api/generated/arquebusiers/arquebusiers';
import type { ArquebusierResponse } from '@/api/generated/model';
import { ConfirmFailure } from '@/components/app/confirm-failure';
import { PhotoUploadFailure } from '@/components/app/photo-image';
import { PhotoUpload } from '@/components/app/PhotoUpload';
import { ID_PHOTO_RULES, LICENSE_PHOTO_RULES, photoUrl, type PhotoSlug } from '../photos';
import { photoProblemMessage, problemCode } from '../problems';

/** Answers that mean the page shows outdated photos: it is refreshed as well as told. */
const STALE = new Set(['photos.notFound', 'photos.noLicense', 'arquebusiers.notFound']);

/** Upload and removal of an arquebusier's photos, with the API's reasons translated. */
function usePhotoActions(arquebusierId: string, onChanged: () => Promise<void>) {
  const { t } = useTranslation('registry');
  const { mutateAsync: upload } = useUploadArquebusierPhoto();
  const { mutateAsync: remove } = useRemoveArquebusierPhoto();
  /** After a successful change: a failed refresh shows the page's own load failure, not a failed change. */
  const refreshQuietly = async () => {
    try {
      await onChanged();
    } catch {
      // The page's query reports its own failure.
    }
  };
  return {
    upload: async (slug: PhotoSlug, photo: Blob) => {
      try {
        await upload({ id: arquebusierId, kind: slug, data: { file: photo } });
      } catch (error) {
        if (STALE.has(problemCode(error) ?? '')) await onChanged();
        throw new PhotoUploadFailure(photoProblemMessage(t, error));
      }
      await refreshQuietly();
    },
    remove: async (slug: PhotoSlug) => {
      try {
        await remove({ id: arquebusierId, kind: slug });
      } catch (error) {
        // Already removed elsewhere: that is what the user wanted.
        if (problemCode(error) === 'photos.notFound') {
          await onChanged();
          return;
        }
        if (STALE.has(problemCode(error) ?? '')) await onChanged();
        throw new ConfirmFailure(photoProblemMessage(t, error));
      }
      await refreshQuietly();
    },
  };
}

interface PhotoProps {
  arquebusier: ArquebusierResponse;
  onChanged: () => Promise<void>;
  /** False while the registry is locked for the caller: the photos are shown, not changed (BR-10). */
  canWrite?: boolean;
}

/**
 * Spec "Photo screens": the ID photo in the record header, 72 px wide; the picture itself opens a
 * menu to replace or remove it, or the file chooser when there is none (spec: Picture actions).
 */
export function IdPhoto({ arquebusier, onChanged, canWrite = true }: PhotoProps) {
  const { t } = useTranslation('registry');
  const actions = usePhotoActions(arquebusier.id, onChanged);
  const name = `${arquebusier.firstName} ${arquebusier.lastName}`;
  return (
    <PhotoUpload
      variant="picture"
      size="compact"
      label={t('photos.idPhoto')}
      photoUrl={photoUrl(arquebusier.id, 'id', arquebusier.photos.id)}
      photoAlt={t('photos.idPhotoAlt', { name })}
      emptyText={t('photos.noIdPhoto')}
      onUpload={(photo) => actions.upload('id', photo)}
      removal={{
        title: t('photos.removeTitle.id'),
        description: t('photos.removeConfirm.description', { name }),
        confirmLabel: t('photos.removeConfirm.confirm'),
        onRemove: () => actions.remove('id'),
      }}
      readOnly={!canWrite}
      {...ID_PHOTO_RULES}
    />
  );
}

/** Spec "Photo screens": both sides of the license, offered only while a license is saved. */
export function LicensePhotos({ arquebusier, onChanged, canWrite = true }: PhotoProps) {
  const { t } = useTranslation('registry');
  const actions = usePhotoActions(arquebusier.id, onChanged);
  const frontId = useId();
  const backId = useId();
  if (arquebusier.license === null) {
    return <p className="text-sm text-muted-foreground">{t('photos.needsLicense')}</p>;
  }
  const name = `${arquebusier.firstName} ${arquebusier.lastName}`;
  const sides = [
    {
      slug: 'license-front',
      titleId: frontId,
      title: t('photos.frontTitle'),
      label: t('photos.frontPhoto'),
      removeTitle: t('photos.removeTitle.licenseFront'),
      photo: arquebusier.photos.licenseFront,
    },
    {
      slug: 'license-back',
      titleId: backId,
      title: t('photos.backTitle'),
      label: t('photos.backPhoto'),
      removeTitle: t('photos.removeTitle.licenseBack'),
      photo: arquebusier.photos.licenseBack,
    },
  ] as const;
  return (
    <div className="grid gap-group *:min-w-0 sm:grid-cols-2">
      {sides.map(({ slug, titleId, title, label, removeTitle, photo }) => (
        <div key={slug} role="group" aria-labelledby={titleId} className="flex flex-col gap-2">
          <p id={titleId} className="text-sm font-medium">
            {title}
          </p>
          <PhotoUpload
            label={label}
            photoUrl={photoUrl(arquebusier.id, slug, photo)}
            photoAlt={t('photos.licensePhotoAlt', { photo: title, name })}
            emptyText={t('photos.noLicensePhoto')}
            onUpload={(image) => actions.upload(slug, image)}
            removal={{
              title: removeTitle,
              description: t('photos.removeConfirm.description', { name }),
              confirmLabel: t('photos.removeConfirm.confirm'),
              onRemove: () => actions.remove(slug),
            }}
            readOnly={!canWrite}
            {...LICENSE_PHOTO_RULES}
          />
        </div>
      ))}
    </div>
  );
}
