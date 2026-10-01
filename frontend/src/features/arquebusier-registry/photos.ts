import { getGetArquebusierPhotoUrl } from '@/api/generated/arquebusiers/arquebusiers';
import type { ArquebusierPhotoResponse } from '@/api/generated/model';
import type { PhotoRules } from '@/components/app/PhotoUpload';

/** URL slugs of the photo kinds (design D4). */
export type PhotoSlug = 'id' | 'license-front' | 'license-back';

/** NFR-15: 3:4 portrait, at least 600 × 800, stored at most 1200 × 1600 (the server's rules). */
export const ID_PHOTO_RULES: PhotoRules = {
  aspect: 3 / 4,
  minWidth: 600,
  minHeight: 800,
  maxWidth: 1200,
  maxHeight: 1600,
};

/** License sides: long side at least 800, sides within a factor of 2, at most 2000 (the server's rules). */
export const LICENSE_PHOTO_RULES: PhotoRules = {
  maxSideRatio: 2,
  minLongSide: 800,
  maxWidth: 2000,
  maxHeight: 2000,
};

/**
 * The image URL of a photo, or null when there is none. The version changes with every upload, so
 * the browser loads a replaced photo instead of an old copy; it carries no personal data.
 */
export function photoUrl(
  arquebusierId: string,
  slug: PhotoSlug,
  photo: ArquebusierPhotoResponse | null,
): string | null {
  return photo ? `${getGetArquebusierPhotoUrl(arquebusierId, slug)}?v=${photo.version}` : null;
}
