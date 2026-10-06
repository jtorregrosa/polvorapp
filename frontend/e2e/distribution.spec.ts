import { readFile } from 'node:fs/promises';
import type { Download, Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from './identity';
import { expect, test, waitForShell } from './fixtures';

/**
 * Distribution planning (change add-distribution-planning) against the seeded stack. The current
 * edition has a powder and a weapons day with Cruzados at 09:00 and Abencerrajes at 09:30, a powder
 * proxy in Cruzados' order (Pau Alberola collected by Amparo Pastor) and a weapons proxy in
 * Abencerrajes'. In Cruzados' order Remedios Lledó has no license and Youssef El Amrani a pending
 * one, so neither can be a
 * proxy. Each project registers a proxy for a holder of its own and removes it, so parallel
 * projects never collide; a proxy left by a failed run is removed before and after the test.
 */

/** The past edition (closed in the seed): read-only for FiringChiefs. */
const PAST_EDITION = '0193a500-0000-7000-8000-000000000001';

/**
 * A holder of Norte's order with powder and no proxy, one per project. A new project, or
 * `--repeat-each` within one, needs a holder of its own.
 */
function holderOf(projectName: string): string {
  return projectName === 'mobile-360' ? 'El Amrani, Youssef' : 'Sempere Llorens, Vicent';
}

async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

/** Removes the powder proxy of `holder` that a failed run may have left. */
async function removeLeftoverProxy(page: Page, holder: string): Promise<void> {
  const current = (await (await page.request.get('/api/editions/current')).json()) as {
    edition: { id: string };
  };
  const proxies = (await (
    await page.request.get(`/api/distribution/editions/${current.edition.id}/proxies`)
  ).json()) as { id: string; type: string; holder: { name: string } }[];
  for (const proxy of proxies.filter(
    (candidate) => candidate.holder.name === holder && candidate.type === 'POWDER',
  )) {
    const removed = await page.request.delete(`/api/distribution/proxies/${proxy.id}`, {
      headers: await antiforgeryHeaders(page),
    });
    expect([204, 404], 'removing a proxy left by a failed run').toContain(removed.status());
  }
}

async function saved(download: Promise<Download>): Promise<{ name: string; bytes: Buffer }> {
  const file = await download;
  const path = await file.path();
  return { name: file.suggestedFilename(), bytes: await readFile(path) };
}

async function openDistribution(page: Page, path = '/distribution') {
  // The navigation entry opens the edition in progress (a drawer on phones: go there directly).
  await page.goto(path);
  await waitForShell(page);
  await expect(page.getByRole('heading', { level: 1, name: /^Reparto de \d{4}$/ })).toBeVisible();
}

/** Polled: the layout settles after the data and the fonts. */
async function expectNoSidewaysScroll(page: Page) {
  await expect
    .poll(() =>
      page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth),
    )
    .toBe(true);
}

test.describe('as the seeded FiringChief of Norte', () => {
  test.use({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES' });

  test.beforeEach(async ({ page }, testInfo) => {
    await removeLeftoverProxy(page, holderOf(testInfo.project.name));
  });

  test.afterEach(async ({ page }, testInfo) => {
    await removeLeftoverProxy(page, holderOf(testInfo.project.name));
  });

  test('sees their slots and proxies, registers a proxy, prints its form and removes it', async ({
    page,
    axeViolations,
  }, testInfo) => {
    const holder = holderOf(testInfo.project.name);
    await openDistribution(page);

    const powder = page.getByRole('region', { name: 'Día de reparto de pólvora', exact: true });
    await expect(powder).toContainText('Cruzados');
    await expect(powder).toContainText('09:00');
    await expect(powder).not.toContainText('Abencerrajes');
    await expect(powder.getByRole('button', { name: /Descargar/ })).toHaveCount(0);
    const proxies = page.getByRole('region', { name: 'Autorizados de recogida' });
    await expect(proxies).toContainText('Alberola Navarro, Pau');
    await expect(proxies).toContainText('Pastor Gomis, Amparo');
    // Sur's weapons proxy is not Norte's.
    await expect(proxies).not.toContainText('Abencerrajes');
    await expectNoSidewaysScroll(page);
    expect(await axeViolations()).toEqual([]);

    await proxies.getByRole('button', { name: 'Añadir autorizado' }).click();
    const panel = page.getByRole('dialog', { name: 'Añadir un autorizado de recogida' });
    await expect(panel).toContainText('El motivo no se guarda');
    if (testInfo.project.name === 'mobile-360') {
      await expect(panel).toHaveAttribute('data-side', 'bottom');
    }
    await panel.getByRole('radio', { name: /^Pólvora/ }).check();
    await panel.getByRole('combobox', { name: /Titular/ }).selectOption({ label: holder });
    const proxy = panel.getByRole('combobox', { name: /Autorizado/ });
    await expect(
      proxy.getByRole('option', { name: /^Lledó Pérez, Remedios \(no puede: sin licencia/ }),
    ).toBeDisabled();
    if (holder !== 'El Amrani, Youssef') {
      // A pending license is not an active one.
      await expect(
        proxy.getByRole('option', { name: /^El Amrani, Youssef \(no puede: sin licencia/ }),
      ).toBeDisabled();
    }
    expect(await axeViolations(page, '[role="dialog"]')).toEqual([]);
    await proxy.selectOption({ label: 'Pastor Gomis, Amparo' });
    await panel.getByRole('button', { name: 'Guardar cambios' }).click();
    await expect(panel).toBeHidden();

    const download = page.waitForEvent('download');
    await proxies.getByRole('button', { name: `Imprimir formulario de ${holder} (pólvora)` }).click();
    const form = await saved(download);
    expect(form.name).toMatch(/^polvorapp-\d{4}-pickup-authorisation-powder-cruzados-[0-9a-f]{8}\.pdf$/);
    expect(form.bytes.subarray(0, 5).toString()).toBe('%PDF-');

    await proxies.getByRole('button', { name: `Eliminar el autorizado de pólvora de ${holder}` }).click();
    const confirmation = page.getByRole('alertdialog');
    expect(await axeViolations(page, '[role="alertdialog"]')).toEqual([]);
    await confirmation.getByRole('button', { name: 'Eliminar autorizado' }).click();
    await expect(
      proxies.getByRole('button', { name: `Imprimir formulario de ${holder} (pólvora)` }),
    ).toHaveCount(0);
  });

  test('reads a closed edition without being offered any change', async ({ page, axeViolations }) => {
    await openDistribution(page, `/editions/${PAST_EDITION}/distribution`);

    await expect(page.getByText(/La edición no está en curso/).first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'Añadir autorizado' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: /^(Planificar|Editar|Eliminar)/ })).toHaveCount(0);
    expect(await axeViolations()).toEqual([]);
  });
});

test('the Admin sees every comparsa on the plan, and the comparsas without a validated order', async ({
  page,
  axeViolations,
}) => {
  await openDistribution(page);

  const powder = page.getByRole('region', { name: 'Día de reparto de pólvora', exact: true });
  await expect(powder).toContainText('Abencerrajes');
  // The comparsas without a slot and without a validated order are folded under one line.
  await expect(powder).toContainText(/\d+ comparsas? sin turno/);
  await expect(powder.getByText('Pedidos sin validar')).toBeVisible();
  await powder.getByText(/^Ver (la comparsa|las \d+ comparsas)$/).click();
  await expect(powder.getByRole('listitem').filter({ hasText: 'Abencerrajes (en borrador)' })).toBeVisible();
  await expect(
    powder.getByRole('button', { name: 'Descargar el listado de pólvora en Excel' }),
  ).toBeVisible();
  await expectNoSidewaysScroll(page);
  expect(await axeViolations()).toEqual([]);
});
