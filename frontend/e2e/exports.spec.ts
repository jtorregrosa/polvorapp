import { readFile } from 'node:fs/promises';
import type { Download, Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from './identity';
import { expect, test, waitForShell } from './fixtures';

/**
 * Exports (change add-exports) against the seeded stack, read only: the current edition has Norte
 * submitted, Sur in draft and Este not prepared, so no recipient export has rows yet and Norte's
 * list is a draft. The serial review cycle (`serial-state/order-review.spec.ts`) downloads Norte's
 * final list once it validates the order.
 */

const CURRENT_NORTE_ORDER = '0193a700-0000-7000-8000-000000000003';

async function saved(download: Promise<Download>): Promise<{ name: string; bytes: Buffer }> {
  const file = await download;
  const path = await file.path();
  return { name: file.suggestedFilename(), bytes: await readFile(path) };
}

async function openExports(page: Page) {
  await page.goto('/orders');
  await waitForShell(page);
  await page.getByRole('link', { name: 'Exportaciones' }).click();
  await expect(page.getByRole('heading', { level: 1, name: /^Exportaciones de \d{4}$/ })).toBeVisible();
}

test('the Admin downloads the Arms Authority export as Excel and as PDF', async ({ page, axeViolations }) => {
  await openExports(page);

  await expect(page.getByText('Formatos provisionales')).toBeVisible();
  await expect(page.getByText('Comparsas sin pedido validado')).toBeVisible();
  await expect(
    page.getByRole('listitem').filter({ hasText: 'Comparsa Sintética Norte (enviado)' }),
  ).toBeVisible();
  await expect(
    page.getByRole('listitem').filter({ hasText: 'Comparsa Sintética Este (sin preparar)' }),
  ).toBeVisible();

  const authority = page.getByRole('region', { name: 'Intervención de Armas' });
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth),
  ).toBe(true);
  expect(await axeViolations()).toEqual([]);

  const xlsx = page.waitForEvent('download');
  await authority.getByRole('button', { name: 'Descargar Intervención de Armas en Excel' }).click();
  const workbook = await saved(xlsx);
  expect(workbook.name).toMatch(/^polvorapp-\d{4}-arms-authority-provisional\.xlsx$/);
  expect(workbook.bytes.subarray(0, 2).toString()).toBe('PK');

  const pdf = page.waitForEvent('download');
  await authority.getByRole('button', { name: 'Descargar Intervención de Armas en PDF' }).click();
  const pdfFile = await saved(pdf);
  expect(pdfFile.name).toMatch(/^polvorapp-\d{4}-arms-authority-provisional\.pdf$/);
  expect(pdfFile.bytes.subarray(0, 5).toString()).toBe('%PDF-');
});

test.describe('as the seeded FiringChief of Norte', () => {
  test.use({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES' });

  test('downloads the list of her submitted order as a draft', async ({ page }) => {
    await page.goto(`/orders/${CURRENT_NORTE_ORDER}`);
    await waitForShell(page);

    const list = page.getByRole('region', { name: 'Lista del pedido' });
    await expect(list).toContainText('Es un borrador');
    const download = page.waitForEvent('download');
    await list
      .getByRole('button', { name: /^Descargar la lista de Comparsa Sintética Norte \(borrador\) en PDF/ })
      .click();
    const file = await saved(download);

    expect(file.name).toMatch(
      /^polvorapp-\d{4}-comparsa-list-comparsa-sintetica-norte-draft-provisional\.pdf$/,
    );
    expect(file.bytes.subarray(0, 5).toString()).toBe('%PDF-');
    await expect(page.getByRole('link', { name: 'Exportaciones' })).toHaveCount(0);
  });

  test('cannot open the exports page', async ({ page }) => {
    await page.goto(`/orders/${CURRENT_NORTE_ORDER}`);
    await waitForShell(page);
    const editionId = await page.evaluate(async (orderId) => {
      const response = await fetch(`/api/comparsa-orders/${orderId}`, { credentials: 'same-origin' });
      return ((await response.json()) as { edition: { id: string } }).edition.id;
    }, CURRENT_NORTE_ORDER);

    await page.goto(`/editions/${editionId}/exports`);

    await expect(page.getByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeVisible();
  });
});
