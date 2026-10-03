import { getGetComparsaLogoUrl } from '@/api/generated/comparsas/comparsas';
import { getGetFederationLogoUrl } from '@/api/generated/federation/federation';
import type { ComparsaLogoResponse } from '@/api/generated/model';
import type { PhotoRules } from '@/components/app/PhotoUpload';

/**
 * The logo rules of the server (spec: Logo validation and processing; LogoStorage): any shape whose
 * long side is 256 to 1024 px and at most 3 times the short side, kept as PNG with transparency.
 */
export const LOGO_RULES: PhotoRules = {
  maxSideRatio: 3,
  minLongSide: 256,
  maxWidth: 1024,
  maxHeight: 1024,
  output: 'png',
};

/** The logo's URL, versioned so the browser reloads it when it changes; null without a logo. */
export function logoUrl(comparsaId: string, logo: ComparsaLogoResponse | null): string | null {
  return logo ? `${getGetComparsaLogoUrl(comparsaId)}?v=${encodeURIComponent(logo.version)}` : null;
}

/** The Federation logo's URL, versioned like a comparsa's; null without a logo. */
export function federationLogoUrl(logo: ComparsaLogoResponse | null): string | null {
  return logo ? `${getGetFederationLogoUrl()}?v=${encodeURIComponent(logo.version)}` : null;
}
