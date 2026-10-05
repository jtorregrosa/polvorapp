import type { Page } from '@playwright/test';
import writeXlsxFile from 'write-excel-file/node';
import { FIRING_CHIEF_STATE } from './identity';
import { expect, test as base, waitForShell } from './fixtures';

/**
 * Spreadsheet import (change add-registry-import, UC-09) against the seeded stack. Workbooks are
 * built at run time with fresh synthetic identities (SEC-11: no workbook file is committed), and
 * the imported arquebusiers are deleted afterwards. The flow imports into the shared registry, so it
 * runs in the desktop Chromium project only: parallel projects never import the same people.
 */

const NORTE = 'Cruzados';
const NORTE_ID = '0193a100-0000-7000-8000-000000000001';
const LETTERS = 'TRWAGMYFPDXBNJZSQVHLCKE';
const XLSX = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
const HEADERS = [
  'ID Unión',
  'Apellidos',
  'Nombre',
  'DNI/NIE',
  'Fecha de nacimiento',
  'Género',
  'Correo electrónico',
  'Teléfono',
  'Estado',
  'Tipo de licencia',
  'Fecha de expedición',
  'Fecha de caducidad',
  'Fecha del curso',
];

let identities = 0;

/**
 * A valid synthetic identity, unique across calls and runs of the last day, in ranges the other
 * specs and the seed do not use: DNI numbers from 9 100 000 and federation ids from 400 000.
 */
function syntheticIdentity(): { nationalId: string; federationId: number; lastName: string } {
  const second = Math.floor(Date.now() / 1000) % 100_000;
  identities += 1;
  const number = 9_100_000 + ((second * 7 + identities) % 800_000);
  return {
    nationalId: `${String(number).padStart(8, '0')}${LETTERS[number % LETTERS.length]}`,
    federationId: 400_000 + number - 9_100_000,
    lastName: `Importada E2E ${number}`,
  };
}

type Cell = {
  value: string | number | Date;
  type?: typeof String | typeof Number | typeof Date;
  format?: string;
} | null;

const date = (iso: string): Cell => ({
  value: new Date(`${iso}T00:00:00Z`),
  type: Date,
  format: 'dd/mm/yyyy',
});

/** A template-shaped workbook: the Spanish headers, then `rows`. */
async function workbook(rows: Cell[][]): Promise<{ name: string; mimeType: string; buffer: Buffer }> {
  const header = HEADERS.map((value): Cell => ({ value, type: String }));
  const buffer = await writeXlsxFile([header, ...rows]).toBuffer();
  return { name: 'arcabuceros-norte.xlsx', mimeType: XLSX, buffer };
}

/** A complete row: license issued and course done, so its only warnings are the photo ones (not reported). */
function row(identity: ReturnType<typeof syntheticIdentity>, course = true): Cell[] {
  return [
    { value: identity.federationId, type: Number },
    { value: identity.lastName, type: String },
    { value: 'Arcabucera', type: String },
    { value: identity.nationalId, type: String },
    date('1990-05-01'),
    { value: 'Mujer', type: String },
    null,
    null,
    null,
    { value: 'AE', type: String },
    date('2024-03-10'),
    null,
    course ? date('2023-11-04') : null,
  ];
}

async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

const test = base.extend<{
  /** Deletes, after the test, every arquebusier of Norte whose last name starts with one of these. */
  deleteImported: (lastName: string) => void;
}>({
  deleteImported: async ({ page }, use) => {
    const names: string[] = [];
    await use((lastName) => names.push(lastName));
    if (names.length === 0) return;
    const list = await page.request.get(`/api/arquebusiers?comparsaId=${NORTE_ID}`);
    const rows = (await list.json()) as { id: string; lastName: string }[];
    const headers = await antiforgeryHeaders(page);
    const failed: string[] = [];
    for (const imported of rows.filter((candidate) => names.includes(candidate.lastName))) {
      const response = await page.request.delete(`/api/arquebusiers/${imported.id}`, { headers });
      if (![204, 404].includes(response.status())) failed.push(`${imported.id}: ${response.status()}`);
    }
    expect(failed, 'cleanup of imported arquebusiers').toEqual([]);
  },
});

/** Flows that import into the shared registry run in one project only. */
function desktopOnly() {
  test.skip(
    test.info().project.name !== 'desktop-chromium',
    'Imports into the shared registry: one project only.',
  );
}

async function openImport(page: Page) {
  await page.goto('/arquebusiers');
  await waitForShell(page);
  await page.getByRole('link', { name: 'Importar' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Importar arcabuceros' })).toBeVisible();
  await page.getByRole('combobox', { name: 'Comparsa' }).selectOption({ label: NORTE });
}

async function checkFile(page: Page, file: { name: string; mimeType: string; buffer: Buffer }) {
  await page.locator('input[type="file"]').setInputFiles(file);
  await page.getByRole('button', { name: 'Comprobar el fichero' }).click();
}

test.describe('arquebusier import as an Admin', () => {
  test.beforeEach(desktopOnly);

  test('downloads the template', async ({ page }) => {
    await openImport(page);

    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Descargar la plantilla' }).click();

    expect((await download).suggestedFilename()).toBe('polvorapp-arquebusiers-template.xlsx');
  });

  test('reports the errors of a file and does not import it', async ({ page, axeViolations }) => {
    const first = syntheticIdentity();
    const second = syntheticIdentity();
    const wrongLetter = {
      ...first,
      nationalId: `${first.nationalId.slice(0, 8)}${first.nationalId.endsWith('A') ? 'B' : 'A'}`,
    };
    await openImport(page);

    await checkFile(
      page,
      await workbook([row(wrongLetter), row({ ...second, federationId: first.federationId })]),
    );

    const table = page.getByRole('table', { name: 'Filas con errores o avisos' });
    await expect(table.getByText('DNI/NIE: La letra no corresponde a los números.')).toBeVisible();
    await expect(table.getByText('ID Unión: Se repite en otra fila del fichero.')).toHaveCount(2);
    await expect(page.getByText(/2 filas tienen errores/)).toBeVisible();
    await expect(page.getByRole('button', { name: /^Importar \d/ })).toHaveCount(0);
    expect(await axeViolations()).toEqual([]);
  });

  test('checks a file with warnings, imports it and opens the comparsa list', async ({
    page,
    deleteImported,
  }) => {
    test.slow();
    const withCourse = syntheticIdentity();
    const withoutCourse = syntheticIdentity();
    deleteImported(withCourse.lastName);
    deleteImported(withoutCourse.lastName);
    await openImport(page);

    await checkFile(page, await workbook([row(withCourse), row(withoutCourse, false)]));

    // The outcome comes first and takes focus, so it is announced and in view.
    await expect
      .poll(() => page.evaluate(() => document.activeElement?.textContent ?? ''))
      .toContain('El fichero se puede importar: 2 arcabuceros.');
    await expect(page.getByText('Aviso: Sin curso')).toBeVisible();
    await page.getByRole('button', { name: 'Importar 2 arcabuceros' }).click();
    const dialog = page.getByRole('alertdialog', { name: `¿Importar 2 arcabuceros en ${NORTE}?` });
    await dialog.getByRole('button', { name: 'Importar 2 arcabuceros' }).click();

    await expect(page.getByText(`2 arcabuceros importados en ${NORTE}.`)).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`/arquebusiers\\?comparsaId=${NORTE_ID}$`));
    await page
      .getByRole('link', { name: new RegExp(withCourse.lastName) })
      .first()
      .click();
    await waitForShell(page);
    await expect(page.getByText('AE (avancarga)').first()).toBeVisible();
  });
});

test.describe('arquebusier import on a phone', () => {
  test.beforeEach(() => {
    test.skip(test.info().project.name !== 'mobile-360', 'The 360 px layout check.');
  });

  // Checking stores nothing, so this can run beside the desktop flows.
  test('checks a file at 360 px without horizontal scrolling', async ({ page, axeViolations }) => {
    const identity = syntheticIdentity();
    await openImport(page);

    await checkFile(page, await workbook([row(identity, false)]));

    await expect(page.getByText('El fichero se puede importar: 1 arcabucero.')).toBeVisible();
    await expect(page.getByText('Aviso: Sin curso')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Importar 1 arcabucero' })).toBeVisible();
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(0);
    expect(await axeViolations()).toEqual([]);
  });
});

test.describe('arquebusier import as a FiringChief', () => {
  test.use({ storageState: FIRING_CHIEF_STATE });
  test.beforeEach(desktopOnly);

  test('is neither offered nor reachable', async ({ page }) => {
    await page.goto('/arquebusiers');
    await waitForShell(page);
    await expect(page.getByRole('link', { name: 'Importar' })).toHaveCount(0);

    await page.goto('/arquebusiers/import');

    await expect(page.getByRole('heading', { name: 'Acceso no permitido' })).toBeVisible();
  });
});
