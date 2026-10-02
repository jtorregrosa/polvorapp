import type { Browser, Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from '../identity';
import { expect, test, waitForShell } from '../fixtures';

/**
 * Orders of the current edition (BR-10, UC-10; change add-festival-editions). Closing them changes
 * what every FiringChief sees, so this spec runs in the `serial-state` project, after the other
 * projects, and opens the seeded current edition's orders again even when it fails midway.
 */

const SEEDED_CURRENT_ID = '0193a500-0000-7000-8000-000000000002';

async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

test.afterEach(async ({ page }) => {
  const edition = (await (await page.request.get(`/api/editions/${SEEDED_CURRENT_ID}`)).json()) as {
    ordersOpen: boolean;
    version: number;
  };
  if (edition.ordersOpen) return;
  const response = await page.request.post(`/api/editions/${SEEDED_CURRENT_ID}/orders`, {
    headers: await antiforgeryHeaders(page),
    data: { open: true, version: edition.version },
  });
  expect(response.ok(), 'opening the orders again after the test').toBe(true);
});

/** The current edition card on the start page, as the seeded FiringChief sees it. */
async function chiefCardText(browser: Browser): Promise<string> {
  const context = await browser.newContext({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES' });
  try {
    const chief = await context.newPage();
    await chief.goto('/');
    await waitForShell(chief);
    const card = chief.getByRole('region', { name: 'Edición actual' });
    await expect(card).toBeVisible();
    return await card.innerText();
  } finally {
    await context.close();
  }
}

test('the Admin closes the orders of the current edition and opens them again', async ({ page, browser }) => {
  await page.goto(`/editions/${SEEDED_CURRENT_ID}`);
  await waitForShell(page);

  await page.getByRole('button', { name: 'Cerrar pedidos' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Cerrar pedidos' }).click();
  await expect(page.getByRole('status').filter({ hasText: /Pedidos de \d{4} cerrados\./ })).toHaveCount(1);
  await expect(page.getByText('Los pedidos son de solo lectura para los jefes de disparo')).toBeVisible();
  expect(await chiefCardText(browser)).toContain('Pedidos cerrados');

  await page.getByRole('button', { name: 'Abrir pedidos' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Abrir pedidos' }).click();
  await expect(page.getByRole('status').filter({ hasText: /Pedidos de \d{4} abiertos\./ })).toHaveCount(1);
  await expect(page.getByText('Los jefes de disparo pueden editar los pedidos')).toBeVisible();
  expect(await chiefCardText(browser)).toContain('Pedidos abiertos');
});
