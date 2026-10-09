import { randomUUID } from 'node:crypto';
import type { Page } from '@playwright/test';
import { ADMIN_STATE } from '../identity';
import { expect, openUserMenu, test, waitForShell } from '../fixtures';
import {
  antiforgeryHeaders,
  currentPlan,
  powderOf,
  restoreAll,
  restoreOrder,
  saved,
  validateNorte,
  workbookText,
  type Day,
} from './distribution-support';

/**
 * The powder day's offline handover capture (change add-offline-distribution-capture, UC-21, D9):
 * the Admin prepares the device, captures without connectivity from the installed app's precache,
 * syncs, meets a conflict made by another device, downloads the filled list, is warned before
 * signing out with handovers unsynced, and undoes one. It validates Norte's order and records
 * handovers other specs must not see, so it runs in `serial-state` and undoes everything after.
 */

interface CaptureRow {
  number: number;
  entryId: string;
  flask: 'OWNED' | 'RENTAL_1KG' | 'RENTAL_2KG' | 'NONE';
}

interface CapturePackage {
  rows: CaptureRow[];
  handovers: { id: string; version: number }[];
}

const SYNC_ROUTE = '**/api/distribution/distributions/*/handovers/sync';

async function capturePackage(page: Page, day: Day): Promise<CapturePackage> {
  const response = await page.request.get(`/api/distribution/distributions/${day.id}/capture`);
  expect(response.ok(), 'reading the capture package').toBe(true);
  return (await response.json()) as CapturePackage;
}

/** Undoes every handover of the powder day: the seed's current edition has none. */
async function undoHandovers(page: Page): Promise<void> {
  const day = powderOf(await currentPlan(page));
  const { handovers } = await capturePackage(page, day);
  const headers = await antiforgeryHeaders(page);
  for (const handover of handovers) {
    const undone = await page.request.delete(
      `/api/distribution/handovers/${handover.id}?version=${String(handover.version)}`,
      { headers },
    );
    expect([204, 404], 'undoing a handover').toContain(undone.status());
  }
}

const holderButton = (page: Page, number: number) =>
  page.getByRole('button', { name: new RegExp(`^Nº ${String(number)} · `) });

async function record(page: Page, number: number, flask?: string): Promise<void> {
  await holderButton(page, number).click();
  const panel = page.getByRole('dialog', { name: /^Entrega de / });
  if (flask !== undefined) await panel.getByLabel('Nº de cantimplora').fill(flask);
  await panel.getByRole('button', { name: 'Registrar entrega' }).click();
  await expect(panel).toBeHidden();
}

test.afterEach(async ({ page, browser }) => {
  // Leave the capture screen first: its sync must not record a handover after they are undone.
  await page.goto('about:blank').catch(() => undefined);
  await restoreAll([
    ['handovers', () => undoHandovers(page)],
    ['order', () => restoreOrder(page, browser)],
  ]);
});

test('the Admin captures handovers offline, syncs them and meets a conflict from another device', async ({
  page,
  browser,
  context,
  axeViolations,
}) => {
  test.setTimeout(180_000);
  await validateNorte(page);
  const day = powderOf(await currentPlan(page));
  const { rows } = await capturePackage(page, day);
  const rented = rows.filter((row) => row.flask === 'RENTAL_1KG' || row.flask === 'RENTAL_2KG');
  const others = rows.filter((row) => row.flask === 'OWNED' || row.flask === 'NONE');
  const [first, second] = rented;
  const [third, fourth] = others;
  if (!first || !second || !third || !fourth) throw new Error('Norte needs two rented flasks and two others');

  // Prepare the device while online; the installed app's worker then serves the shell.
  await page.goto('/distribution');
  await waitForShell(page);
  const powder = page.getByRole('region', { name: 'Día de reparto de pólvora', exact: true });
  await powder.getByRole('button', { name: 'Preparar para captura sin conexión' }).click();
  await expect(powder.getByText(/La lista del reparto está guardada en este dispositivo/)).toBeVisible();
  await expect
    .poll(() => page.evaluate(() => navigator.serviceWorker.controller !== null), { timeout: 30_000 })
    .toBe(true);
  await powder.getByRole('link', { name: 'Abrir captura' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Captura de entregas' })).toBeVisible();
  const captureUrl = page.url();

  // Offline: the screen reloads from the precache and captures on the device.
  await context.setOffline(true);
  await page.reload();
  await expect(page.getByRole('heading', { level: 1, name: 'Captura de entregas' })).toBeVisible();
  await expect(page.getByText('Sin conexión: las entregas se guardan en este dispositivo')).toBeVisible();
  await record(page, first.number, 'E2E-117');
  await expect(holderButton(page, first.number)).toContainText('Pendiente de sincronizar');

  // The same flask for another holder is refused on the device, naming who has it.
  await holderButton(page, second.number).click();
  const panel = page.getByRole('dialog', { name: /^Entrega de / });
  await panel.getByLabel('Nº de cantimplora').fill('e2e-117');
  await panel.getByRole('button', { name: 'Registrar entrega' }).click();
  await expect(
    panel.getByText(`La cantimplora e2e-117 ya se ha entregado al número ${String(first.number)}.`).first(),
  ).toBeVisible();
  await panel.getByLabel('Nº de cantimplora').fill('E2E-118');
  await panel.getByRole('button', { name: 'Registrar entrega' }).click();
  await expect(panel).toBeHidden();
  await expect(page.getByText('2 pendientes de sincronizar', { exact: true })).toBeVisible();
  expect(await axeViolations()).toEqual([]);

  // What was captured offline survives reopening the screen offline.
  await page.reload();
  await expect(holderButton(page, first.number)).toContainText('Pendiente de sincronizar');
  await expect(page.getByText('2 pendientes de sincronizar', { exact: true })).toBeVisible();

  // Back online: the session is checked again and the handovers sync.
  await context.setOffline(false);
  await expect(page.getByText('0 pendientes de sincronizar', { exact: true })).toBeVisible({
    timeout: 30_000,
  });
  await expect(holderButton(page, first.number)).toContainText('Entregada');
  await expect(holderButton(page, second.number)).toContainText('Entregada');

  // Another device records a holder first; this one's handover becomes a conflict.
  const other = await browser.newContext({ storageState: ADMIN_STATE, locale: 'es-ES' });
  try {
    const otherPage = await other.newPage();
    const recorded = await otherPage.request.post(
      `/api/distribution/distributions/${day.id}/handovers/sync`,
      {
        headers: await antiforgeryHeaders(otherPage),
        data: {
          handovers: [
            {
              id: randomUUID(),
              holderEntryId: third.entryId,
              distributionNumber: third.number,
              collectedBy: 'HOLDER',
              collectorEntryId: null,
              rentalFlaskNumber: null,
              traceability1: null,
              traceability2: null,
              collectedAt: new Date().toISOString(),
            },
          ],
        },
      },
    );
    expect(recorded.ok(), 'recording from the other device').toBe(true);
  } finally {
    await other.close();
  }
  await record(page, third.number);
  const conflicts = page.getByRole('region', { name: 'Conflictos' });
  await expect(conflicts.getByText('Otro dispositivo ya registró la entrega de este titular.')).toBeVisible({
    timeout: 30_000,
  });
  expect(await axeViolations()).toEqual([]);
  await conflicts.getByRole('button', { name: /^Descartar/ }).click();
  const discard = page.getByRole('alertdialog');
  await discard.getByRole('button', { name: 'Descartar' }).click();
  // While the dialog is open the page behind it is hidden from assistive technology: wait for it to go.
  await expect(discard).toBeHidden();
  await expect(conflicts).toBeHidden();
  await expect(holderButton(page, third.number)).toContainText('Por entregar');

  // The powder list is filled from the handovers.
  await page.goto('/distribution');
  await waitForShell(page);
  const download = page.waitForEvent('download');
  await page
    .getByRole('region', { name: 'Día de reparto de pólvora', exact: true })
    .getByRole('button', { name: 'Descargar el listado de pólvora en Excel' })
    .click();
  const list = workbookText((await saved(download)).bytes);
  expect(list).toContain('E2E-117');
  expect(list).toContain('E2E-118');

  // A handover that cannot sync yet makes signing out ask first.
  await page.route(SYNC_ROUTE, (route) =>
    route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({ status: 503, title: 'Busy', code: 'distribution.busy' }),
    }),
  );
  await page.goto(captureUrl);
  await expect(page.getByRole('heading', { level: 1, name: 'Captura de entregas' })).toBeVisible();
  await record(page, fourth.number);
  await expect(holderButton(page, fourth.number)).toContainText('Pendiente de sincronizar');
  await page.goto('/');
  await waitForShell(page);
  // Should the warning not appear, the click must not end the session the cleanup needs.
  await page.route('**/api/auth/logout', (route) => route.abort());
  await (await openUserMenu(page)).getByRole('menuitem', { name: 'Cerrar sesión' }).click();
  const warning = page.getByRole('alertdialog', { name: '¿Cerrar sesión con entregas sin sincronizar?' });
  await expect(warning).toContainText('1 entrega de pólvora sin sincronizar');
  expect(await axeViolations(page, '[role="alertdialog"]')).toEqual([]);
  await warning.getByRole('button', { name: 'Cancelar' }).click();
  await page.unroute(SYNC_ROUTE);
  await page.unroute('**/api/auth/logout');

  // Undo a synced handover online, after confirming.
  await page.goto(captureUrl);
  await expect(holderButton(page, fourth.number)).toContainText('Entregada', { timeout: 30_000 });
  await holderButton(page, first.number).click();
  await page
    .getByRole('dialog', { name: /^Entrega de / })
    .getByRole('button', { name: 'Deshacer entrega' })
    .click();
  const undo = page.getByRole('alertdialog');
  await expect(undo).toContainText('E2E-117');
  await undo.getByRole('button', { name: 'Deshacer entrega' }).click();
  await expect(holderButton(page, first.number)).toContainText('Por entregar');
});
