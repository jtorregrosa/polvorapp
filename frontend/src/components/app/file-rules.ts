/** Why a chosen file cannot be used; the page words it for its own files. */
export type FileProblem = 'wrongType' | 'tooLarge';

export interface FileRules {
  /** File name extensions with their dot, comma-separated as in the `accept` attribute, e.g. `.xlsx`. */
  accept: string;
  maxBytes: number;
}

/** The dotted extensions of an `accept` value; anything else (MIME types, wildcards) is a mistake. */
function extensionsOf(accept: string): string[] {
  const extensions = accept
    .split(',')
    .map((extension) => extension.trim().toLowerCase())
    .filter((extension) => extension !== '');
  if (extensions.length === 0 || extensions.some((extension) => !/^\.[a-z0-9]+$/.test(extension))) {
    throw new Error(`File rules accept dotted extensions only, not "${accept}".`);
  }
  return extensions;
}

/**
 * Whether `file` breaks `rules`: the type is checked by its name's extension (browsers report
 * spreadsheet types inconsistently), then the size, up to and including `maxBytes`. Use it in the
 * form's schema, so the problem is shown at the field and in the summary before anything is uploaded.
 */
export function fileProblem(file: File, rules: FileRules): FileProblem | undefined {
  const name = file.name.toLowerCase();
  if (!extensionsOf(rules.accept).some((extension) => name.endsWith(extension))) return 'wrongType';
  return file.size > rules.maxBytes ? 'tooLarge' : undefined;
}

const UNITS = [
  { unit: 'byte', bytes: 1 },
  { unit: 'kilobyte', bytes: 1024 },
  { unit: 'megabyte', bytes: 1024 * 1024 },
] as const;

/**
 * A file size for people, in binary units like the limits the pages state ("2 MB" = 2 × 1024 × 1024
 * bytes): the largest unit whose rounded value is at least 1, with one decimal at most.
 */
export function formatFileSize(
  bytes: number,
  formatNumber: (value: number, options: Intl.NumberFormatOptions) => string,
): string {
  const { unit, bytes: size } =
    [...UNITS].reverse().find((candidate) => bytes >= candidate.bytes) ?? UNITS[0];
  const value = Math.round((bytes / size) * 10) / 10;
  const next = UNITS[UNITS.findIndex((candidate) => candidate.unit === unit) + 1];
  // 1,048,575 bytes is 1024.0 KB once rounded: show 1 MB instead.
  if (next && value >= 1024) return formatFileSize(next.bytes, formatNumber);
  return formatNumber(value, { style: 'unit', unit, unitDisplay: 'short', maximumFractionDigits: 1 });
}
