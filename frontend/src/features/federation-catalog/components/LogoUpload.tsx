import { useTranslation } from 'react-i18next';
import { ConfirmFailure } from '@/components/app/confirm-failure';
import { PhotoUploadFailure } from '@/components/app/photo-image';
import { PhotoUpload } from '@/components/app/PhotoUpload';
import { LOGO_RULES } from '../logos';
import { logoProblemMessage, problemCode } from '../problems';

/** Already translated texts of a logo upload. */
export interface LogoUploadTexts {
  /** What the logo is, for the actions: "logo de la comparsa" gives "Añadir logo de la comparsa". */
  label: string;
  alt: string;
  empty: string;
  uploaded: string;
  removed: string;
  removeTitle: string;
  removeDescription: string;
  removeConfirm: string;
}

interface LogoUploadProps {
  texts: LogoUploadTexts;
  /** The logo's versioned URL, or null without a logo. */
  photoUrl: string | null;
  upload: (image: Blob) => Promise<unknown>;
  remove: () => Promise<unknown>;
  /** Answers that mean the page shows an outdated owner: it is refreshed as well as told. */
  staleCodes: ReadonlySet<string>;
  /** Refreshes the owner and what shows its logo. */
  onChanged: () => Promise<void>;
}

/**
 * An Admin's logo upload, inside its owner's section (specs: Logo display, Federation logo): add,
 * replace and remove a logo.
 * The image is chosen and cropped freely in the browser (the whole image selected at first), kept
 * as PNG with its transparency, and the API's reasons are shown translated in the crop dialog or the
 * confirmation. A logo already removed elsewhere counts as removed.
 */
export function LogoUpload({ texts, photoUrl, upload, remove, staleCodes, onChanged }: LogoUploadProps) {
  const { t } = useTranslation('catalog');

  /** After a successful change: a failed refresh shows the page's own load failure, not a failed change. */
  const refreshQuietly = async () => {
    try {
      await onChanged();
    } catch {
      // The page's query reports its own failure.
    }
  };

  const uploadLogo = async (image: Blob) => {
    try {
      await upload(image);
    } catch (error) {
      if (staleCodes.has(problemCode(error) ?? '')) await refreshQuietly();
      throw new PhotoUploadFailure(logoProblemMessage(t, error));
    }
    await refreshQuietly();
  };

  const removeLogo = async () => {
    try {
      await remove();
    } catch (error) {
      // Already removed elsewhere: that is what the Admin wanted.
      if (problemCode(error) === 'logos.notFound') {
        await refreshQuietly();
        return;
      }
      if (staleCodes.has(problemCode(error) ?? '')) await refreshQuietly();
      throw new ConfirmFailure(logoProblemMessage(t, error));
    }
    await refreshQuietly();
  };

  return (
    <PhotoUpload
      label={texts.label}
      photoUrl={photoUrl}
      photoAlt={texts.alt}
      emptyText={texts.empty}
      uploadedText={texts.uploaded}
      removedText={texts.removed}
      subject="logo"
      onUpload={uploadLogo}
      removal={{
        title: texts.removeTitle,
        description: texts.removeDescription,
        confirmLabel: texts.removeConfirm,
        onRemove: removeLogo,
      }}
      {...LOGO_RULES}
    />
  );
}
