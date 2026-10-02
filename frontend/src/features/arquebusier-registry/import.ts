import type { TFunction } from 'i18next';
import { z } from 'zod';
import { ApiProblemError } from '@/api/http';
import { ComplianceWarning, type ArquebusierImportReport } from '@/api/generated/model';
import { fileProblem } from '@/components/app/file-rules';
import { messages, problemCode, reasonMessage } from './problems';

/** The uploads the import accepts (spec: Import file reading): checked before uploading. */
export const IMPORT_FILE_RULES = { accept: '.xlsx', maxBytes: 2 * 1024 * 1024 } as const;

const FILE_PROBLEM_MESSAGES = {
  wrongType: 'registry:import.file.wrongType',
  tooLarge: 'registry:import.file.tooLarge',
} as const;

/** Spec "Import screen": a comparsa and an `.xlsx` file within the limits. */
export const importSchema = z.object({
  comparsaId: z.string().min(1, messages.choice),
  file: z
    .instanceof(File)
    .nullable()
    .superRefine((file, context) => {
      if (!file) {
        context.addIssue({ code: 'custom', message: messages.required });
        return;
      }
      const problem = fileProblem(file, IMPORT_FILE_RULES);
      if (problem) context.addIssue({ code: 'custom', message: FILE_PROBLEM_MESSAGES[problem] });
    }),
});

export type ImportValues = z.input<typeof importSchema>;

export const EMPTY_IMPORT: ImportValues = { comparsaId: '', file: null };

/** The report's field names (as in a registration error) to the template column texts. */
const COLUMN_KEYS = {
  federationId: 'federationId',
  lastName: 'lastName',
  firstName: 'firstName',
  nationalId: 'nationalId',
  birthDate: 'birthDate',
  gender: 'gender',
  email: 'email',
  phone: 'phone',
  status: 'status',
  'license.type': 'licenseType',
  'license.issuedOn': 'licenseIssuedOn',
  'license.expiresOn': 'licenseExpiresOn',
  trainingCompletedOn: 'trainingCompletedOn',
} as const;

type ColumnField = keyof typeof COLUMN_KEYS;

const isColumnField = (field: string): field is ColumnField => Object.hasOwn(COLUMN_KEYS, field);

/** The template's header for a field, in the active language; the field itself if unknown. */
export function columnLabel(t: TFunction<'registry'>, field: string): string {
  return isColumnField(field) ? t(`import.columns.${COLUMN_KEYS[field]}`) : field;
}

/** "Column: reason", as the report shows an error of a row. */
export function rowErrorText(t: TFunction<'registry'>, field: string, reason: string): string {
  return t('import.report.problem', {
    column: columnLabel(t, field),
    reason: t(reasonMessage(field, reason) as never),
  });
}

const FILE_REASONS = [
  'required',
  'tooLarge',
  'invalid',
  'empty',
  'tooManyRows',
  'missingColumns',
  'duplicateColumns',
] as const;

type FileReason = (typeof FILE_REASONS)[number];

const isFileReason = (reason: string): reason is FileReason =>
  (FILE_REASONS as readonly string[]).includes(reason);

/**
 * Why the server could not read the file at all (a `validation` problem with `file`), in words,
 * with the column headers it names; undefined when the problem is not about the file.
 */
export function fileProblemText(t: TFunction<'registry'>, error: unknown): string | undefined {
  if (!(error instanceof ApiProblemError) || problemCode(error) !== 'validation') return undefined;
  const errors = error.problem?.errors;
  const reason = errors && !Array.isArray(errors) ? errors.file : undefined;
  if (reason === undefined) return undefined;
  if (!isFileReason(reason)) return t('import.fileReasons.invalid');
  const columns = error.problem?.columns;
  const names = Array.isArray(columns)
    ? columns
        .filter((column): column is string => typeof column === 'string')
        .map((column) => columnLabel(t, column))
    : [];
  return t(`import.fileReasons.${reason}`, { columns: names.join(', ') });
}

/** The shape of a report, checked before a refused import's report is shown. */
const reportSchema = z.object({
  rowCount: z.number(),
  validCount: z.number(),
  errorRowCount: z.number(),
  warningRowCount: z.number(),
  ignoredColumns: z.array(z.string()),
  rows: z.array(
    z.object({
      rowNumber: z.number(),
      lastName: z.string().nullable(),
      firstName: z.string().nullable(),
      errors: z.array(z.object({ field: z.string(), reason: z.string() })),
      warnings: z.array(z.enum(ComplianceWarning)),
    }),
  ),
});

/** The report a refused import carries (`arquebusierImport.rowErrors`), if it is one and well formed. */
export function rejectedReport(error: unknown): ArquebusierImportReport | undefined {
  if (!(error instanceof ApiProblemError) || problemCode(error) !== 'arquebusierImport.rowErrors')
    return undefined;
  const parsed = reportSchema.safeParse(error.problem?.report);
  return parsed.success ? parsed.data : undefined;
}
