/** Why a national ID was rejected; the same reasons the API reports (design D4). */
export type NationalIdError = 'required' | 'invalid' | 'checkLetter';

export type NationalIdResult = { value: string } | { error: NationalIdError };

const LETTERS = 'TRWAGMYFPDXBNJZSQVHLCKE';
const NIE_PREFIXES = 'XYZ';
const MAX_INPUT_LENGTH = 64;
const ASCII_LETTER_OR_DIGIT = /^[0-9A-Za-z]$/;
const SHAPE = /^([0-9]{8}|[XYZ][0-9]{7})[A-Z]$/;

/**
 * DNI/NIE validation, mirroring the server (BR-01, design D4) and checked against the same shared
 * vectors: spaces, tabs and hyphens are removed, ASCII letters upper-cased, and any other character
 * makes the value invalid, so look-alike letters from other scripts and full-width digits are
 * rejected rather than folded. JavaScript's own upper-casing is not used on the raw input because it
 * maps some non-ASCII letters (e.g. U+017F) to ASCII ones.
 */
export function parseNationalId(input: string | undefined): NationalIdResult {
  const text = input ?? '';
  if (text.length > MAX_INPUT_LENGTH) {
    return { error: 'invalid' };
  }

  let normalised = '';
  for (const char of text) {
    if (char === ' ' || char === '\t' || char === '-') {
      continue;
    }
    if (!ASCII_LETTER_OR_DIGIT.test(char)) {
      return { error: 'invalid' };
    }
    normalised += char.toUpperCase();
  }

  if (normalised === '') {
    return { error: 'required' };
  }
  if (!SHAPE.test(normalised)) {
    return { error: 'invalid' };
  }

  const prefix = NIE_PREFIXES.indexOf(normalised.charAt(0));
  const digits = prefix >= 0 ? `${prefix}${normalised.slice(1, 8)}` : normalised.slice(0, 8);
  return LETTERS.charAt(Number(digits) % LETTERS.length) === normalised.charAt(8)
    ? { value: normalised }
    : { error: 'checkLetter' };
}
