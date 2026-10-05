import { readFile } from 'node:fs/promises';
import type { Download, Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from './identity';
import { expect, test, waitForShell } from './fixtures';

/**
 * Badges (change add-badges) against the seeded stack, read only: the Admin prints a seeded
 * comparsa's badges (Este, which no other spec empties) and a selection across comparsas; the seeded
 * FiringChief gets no badge action. Nothing is stored by a download, so the desktop and mobile
 * projects can run side by side. Downloads share the Admin's per-user document limit with the
 * exports specs.
 */

const NORTE = 'Cruzados';
const SUR = 'Abencerrajes';
const ESTE = 'Hospitalarios';

async function saved(download: Promise<Download>): Promise<{ name: string; bytes: Buffer }> {
  const file = await download;
  const path = await file.path();
  return { name: file.suggestedFilename(), bytes: await readFile(path) };
}

async function openArquebusiers(page: Page) {
  await page.goto('/arquebusiers');
  await waitForShell(page);
  await expect(page.getByRole('checkbox', { name: 'Seleccionar Sempere Llorens, Vicent' })).toBeVisible();
}

test("the Admin prints a comparsa's badges in Valencian from its page", async ({ page, axeViolations }) => {
  await page.goto('/comparsas');
  await waitForShell(page);
  await page.getByRole('link', { name: ESTE }).first().click();
  await expect(page.getByRole('heading', { level: 1, name: ESTE })).toBeVisible();

  await page.getByRole('button', { name: `Imprimir carnets de ${ESTE}` }).click();
  const sheet = page.getByRole('dialog', { name: 'Imprimir carnets' });
  await expect(sheet).toContainText(`de ${ESTE}, activos y en reserva`);
  await expect(sheet).toContainText('Imprime a escala 100 %');
  expect(await axeViolations()).toEqual([]);

  await expect(sheet.getByRole('radio', { name: 'Español' })).toBeChecked();
  await sheet.getByRole('radio', { name: 'Valenciano' }).click();
  const request = page.waitForRequest((sent) => sent.url().endsWith('/api/badges/sheet'));
  const download = page.waitForEvent('download');
  await sheet.getByRole('button', { name: 'Descargar PDF' }).click();
  expect((await request).postDataJSON()).toMatchObject({ language: 'ca-ES-valencia' });
  const pdf = await saved(download);
  expect(pdf.name).toMatch(/^polvorapp-badges-hospitalarios-\d{8}\.pdf$/);
  expect(pdf.bytes.subarray(0, 5).toString()).toBe('%PDF-');
  await expect(sheet.getByRole('status')).toContainText(pdf.name);
});

test('the Admin selects arquebusiers of two comparsas and prints them', async ({ page, axeViolations }) => {
  await openArquebusiers(page);

  await page.getByRole('combobox', { name: 'Comparsa' }).selectOption({ label: NORTE });
  // Wait for the filtered list before ticking, so the box ticked is the filtered list's.
  await expect(page.getByRole('checkbox', { name: 'Seleccionar Ferrándiz Soler, Mari Carmen' })).toBeHidden();
  await page.getByRole('checkbox', { name: 'Seleccionar Sempere Llorens, Vicent' }).click();
  await page.getByRole('combobox', { name: 'Comparsa' }).selectOption({ label: SUR });
  await expect(page.getByRole('checkbox', { name: 'Seleccionar Sempere Llorens, Vicent' })).toBeHidden();
  await page.getByRole('checkbox', { name: 'Seleccionar Ferrándiz Soler, Mari Carmen' }).click();

  const bar = page.getByRole('region', { name: 'Selección' });
  await expect(bar).toContainText('2 seleccionados');
  expect(await axeViolations()).toEqual([]);
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth),
  ).toBe(true);

  await bar.getByRole('button', { name: /^Imprimir carnets/ }).click();
  const sheet = page.getByRole('dialog', { name: 'Imprimir carnets' });
  await expect(sheet).toContainText('2 arcabuceros seleccionados');
  const download = page.waitForEvent('download');
  await sheet.getByRole('button', { name: 'Descargar PDF' }).click();
  const pdf = await saved(download);
  expect(pdf.name).toMatch(/^polvorapp-badges-selection-2-\d{8}\.pdf$/);
  expect(pdf.bytes.subarray(0, 5).toString()).toBe('%PDF-');
  await page.keyboard.press('Escape');
  await expect(sheet).toBeHidden();
  // The selection is kept after printing, for a reprint or a change.
  await expect(bar).toContainText('2 seleccionados');

  await bar.getByRole('button', { name: 'Quitar la selección' }).click();
  await expect(page.getByRole('region', { name: 'Selección' })).toBeHidden();
});

test.describe('as the seeded FiringChief of Norte', () => {
  test.use({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES' });

  test('there is no selection and no badge action', async ({ page }) => {
    await page.goto('/arquebusiers');
    await waitForShell(page);
    await expect(page.getByRole('link', { name: 'Sempere Llorens, Vicent' })).toBeVisible();

    await expect(page.getByRole('checkbox')).toHaveCount(0);
    await expect(page.getByRole('button', { name: /Imprimir carnets/ })).toHaveCount(0);

    await page.goto('/comparsas');
    await page.getByRole('link', { name: NORTE }).first().click();
    await expect(page.getByRole('heading', { level: 1, name: NORTE })).toBeVisible();
    await expect(page.getByRole('button', { name: /Imprimir carnets/ })).toHaveCount(0);
  });
});
