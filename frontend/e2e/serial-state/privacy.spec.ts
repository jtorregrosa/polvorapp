import { readFile } from 'node:fs/promises';
import type { Browser, Page } from '@playwright/test';
import { FIRING_CHIEF_STATE, inviteFiringChief } from '../identity';
import { expect, openNavigation, test as base, waitForShell } from '../fixtures';

/**
 * Audit log and GDPR requests from end to end (UC-25, UC-26; change add-audit-privacy), as the
 * seeded Admin. It erases people for good, so it only ever erases the synthetic arquebusier and
 * FiringChief it creates itself; it runs in the `serial-state` project because an erasure locks the
 * orders of the edition in progress, which other specs change. Registering needs the registry
 * unlocked: `registry-lock.spec.ts` puts the lock back.
 */

const LETTERS = 'TRWAGMYFPDXBNJZSQVHLCKE';
const NORTE = 'Cruzados';

let identities = 0;

/**
 * A valid synthetic DNI, unique across calls and runs of the last day. The 30M range is this spec's
 * own (the other specs use lower or higher ones), and the federation id is the same number.
 */
function syntheticIdentity(): { nationalId: string; federationId: string; lastName: string } {
  identities += 1;
  const number = 30_000_000 + ((Math.floor(Date.now() / 1000) * 10 + identities) % 1_000_000);
  return {
    nationalId: `${String(number).padStart(8, '0')}${LETTERS[number % LETTERS.length]}`,
    federationId: String(number),
    lastName: `Borrable E2E ${number}`,
  };
}

async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

const test = base.extend<{
  /** Deletes the arquebusiers a test registered, even when it failed midway (404 once erased). */
  deleteAfter: (id: string) => void;
}>({
  deleteAfter: async ({ page }, use) => {
    const ids: string[] = [];
    await use((id) => ids.push(id));
    if (ids.length === 0) return;
    const headers = await antiforgeryHeaders(page);
    const failed: string[] = [];
    for (const id of ids) {
      const response = await page.request.delete(`/api/arquebusiers/${id}`, { headers });
      if (![204, 404].includes(response.status())) failed.push(`${id}: ${response.status()}`);
    }
    expect(failed, 'cleanup of registered arquebusiers').toEqual([]);
  },
});

/** Registers a synthetic arquebusier in Norte through the form. */
async function register(
  page: Page,
  deleteAfter: (id: string) => void,
): Promise<{ id: string; name: string; nationalId: string }> {
  const identity = syntheticIdentity();
  await page.goto('/arquebusiers/new');
  await waitForShell(page);
  await page.getByRole('combobox', { name: 'Comparsa' }).selectOption({ label: NORTE });
  await page.getByLabel(/ID Unión/).fill(identity.federationId);
  await page.getByLabel(/^DNI\/NIE/).fill(identity.nationalId);
  await page.getByLabel(/^Nombre/).fill('Arcabucera');
  await page.getByLabel(/^Apellidos/).fill(identity.lastName);
  await page.getByLabel(/Fecha de nacimiento/).fill('1990-05-01');
  await page.getByRole('radio', { name: 'Mujer' }).click();
  await page.getByRole('button', { name: 'Registrar arcabucero' }).click();
  await expect(page).toHaveURL(/\/arquebusiers\/[0-9a-f-]{36}$/);
  const id = new URL(page.url()).pathname.split('/').pop() ?? '';
  deleteAfter(id);
  return { id, name: `Arcabucera ${identity.lastName}`, nationalId: identity.nationalId };
}

async function lookUp(page: Page, nationalId: string): Promise<void> {
  await page.getByRole('textbox', { name: 'DNI/NIE' }).fill(nationalId);
  await page.getByRole('button', { name: 'Buscar' }).click();
}

/** Every request URL the page makes, to show that a DNI/NIE never travels in one. */
function requestUrls(page: Page): string[] {
  const urls: string[] = [];
  page.on('request', (request) => urls.push(request.url()));
  return urls;
}

async function asFiringChief(browser: Browser) {
  const context = await browser.newContext({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES' });
  return { context, page: await context.newPage() };
}

const outcome = (page: Page, text: RegExp) => page.getByRole('status').filter({ hasText: text });

test.describe('audit log and GDPR requests', () => {
  test('an Admin opens an arquebusier’s history and an entry of it, and filters the log by action', async ({
    page,
    deleteAfter,
    axeViolations,
  }) => {
    const person = await register(page, deleteAfter);

    await page.getByRole('link', { name: 'Ver historial' }).click();
    await expect(page).toHaveURL(new RegExp(`entityType=Arquebusier&entityId=${person.id}`));
    const table = page.getByRole('table', { name: /Entradas del registro de auditoría/ });
    await expect(table.getByRole('rowheader')).toHaveText(['Arcabucero dado de alta']);
    expect(await axeViolations(page)).toEqual([]);

    await table.getByRole('button', { name: /Ver detalles/ }).click();
    const sheet = page.getByRole('dialog', { name: 'Detalles de la entrada' });
    await expect(sheet.getByText('Identificador de traza')).toBeVisible();
    await expect(sheet).toContainText(NORTE);
    await expect(sheet).not.toContainText(person.nationalId);
    expect(await axeViolations(page, '[role="dialog"]')).toEqual([]);
    await sheet.getByRole('button', { name: 'Cerrar' }).click();

    await page.getByRole('button', { name: 'Quitar el filtro del registro' }).click();
    // Unfiltered, the log also holds the sign-ins and other actions of the run.
    await page
      .getByRole('combobox', { name: 'Acción', exact: true })
      .selectOption({ label: 'Arcabucero dado de alta' });
    await expect(page).toHaveURL(/action=ArquebusierRegistered/);
    await expect(table.getByRole('rowheader').first()).toHaveText('Arcabucero dado de alta');
    await expect(table.getByRole('rowheader').filter({ hasNotText: 'Arcabucero dado de alta' })).toHaveCount(
      0,
    );
  });

  test('an Admin downloads and erases a person, after which nothing is held; cancelling changes nothing', async ({
    page,
    deleteAfter,
    axeViolations,
  }) => {
    const person = await register(page, deleteAfter);
    const urls = requestUrls(page);

    await page.goto('/privacy');
    await waitForShell(page);
    await lookUp(page, person.nationalId);
    const summary = page.getByRole('region', { name: 'Datos que guarda PolvorApp' });
    await expect(summary).toContainText(person.name);
    expect(await axeViolations(page)).toEqual([]);

    await summary.getByRole('button', { name: 'Descargar datos' }).click();
    const download = page.getByRole('alertdialog', { name: 'Descargar los datos personales' });
    await download.getByRole('textbox', { name: 'Referencia de la solicitud' }).fill('REQ-E2E-EXPORT');
    const [file] = await Promise.all([
      page.waitForEvent('download'),
      download.getByRole('button', { name: 'Descargar' }).click(),
    ]);
    await expect(download).toBeHidden();
    expect(file.suggestedFilename()).toMatch(/^polvorapp-personal-data-\d{8}\.zip$/);
    const zip = await readFile(await file.path());
    expect(zip.subarray(0, 2).toString('latin1')).toBe('PK');

    // Cancelling the erasure changes nothing.
    await summary.getByRole('button', { name: 'Borrar datos' }).click();
    const erase = page.getByRole('alertdialog', { name: `¿Borrar los datos de ${person.name}?` });
    await expect(erase.getByRole('button', { name: 'Cancelar' })).toBeFocused();
    expect(await axeViolations(page, '[role="alertdialog"]')).toEqual([]);
    await erase.getByRole('button', { name: 'Cancelar' }).click();
    await expect(erase).toBeHidden();
    await expect(summary).toContainText(person.name);

    await summary.getByRole('button', { name: 'Borrar datos' }).click();
    await erase.getByRole('textbox', { name: 'Referencia de la solicitud' }).fill('REQ-E2E-ERASE');
    await erase.getByRole('button', { name: 'Borrar', exact: true }).click();

    await expect(outcome(page, /Datos borrados\./)).toBeVisible();
    await lookUp(page, person.nationalId);
    await expect(page.getByText('PolvorApp no guarda ningún dato de esta persona.')).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);

    // The erasure is in the audit log with its reference, and no request ever carried the DNI/NIE.
    await page.goto('/audit-log?action=PersonalDataErased');
    const row = page
      .getByRole('table', { name: /Entradas del registro de auditoría/ })
      .getByRole('row')
      .nth(1);
    await row.getByRole('button', { name: /Ver detalles/ }).click();
    const sheet = page.getByRole('dialog', { name: 'Detalles de la entrada' });
    await expect(sheet).toContainText('REQ-E2E-ERASE');
    await expect(sheet).not.toContainText(person.nationalId);
    await expect(sheet).not.toContainText(person.name);
    expect(urls.filter((url) => url.toUpperCase().includes(person.nationalId))).toEqual([]);
  });

  test('an Admin downloads a FiringChief’s data, then erases them: shown as erased, only "View history", no sign-in', async ({
    browser,
    page,
  }) => {
    const chief = await inviteFiringChief(browser, page, 'privacy');

    await page.goto(chief.detailPath);
    await waitForShell(page);
    await page.getByRole('button', { name: 'Más acciones' }).click();
    await page.getByRole('menuitem', { name: 'Descargar datos personales' }).click();
    const download = page.getByRole('alertdialog', { name: 'Descargar los datos personales' });
    await download.getByRole('textbox', { name: 'Referencia de la solicitud' }).fill('REQ-E2E-USER-EXPORT');
    const [file] = await Promise.all([
      page.waitForEvent('download'),
      download.getByRole('button', { name: 'Descargar' }).click(),
    ]);
    expect(file.suggestedFilename()).toMatch(/^polvorapp-personal-data-\d{8}\.zip$/);
    await expect(download).toBeHidden();

    await page.getByRole('button', { name: 'Más acciones' }).click();
    await page.getByRole('menuitem', { name: 'Borrar datos personales' }).click();
    const erase = page.getByRole('alertdialog', { name: `¿Borrar los datos de ${chief.name}?` });
    await erase.getByRole('textbox', { name: 'Referencia de la solicitud' }).fill('REQ-E2E-USER');
    await erase.getByRole('button', { name: 'Borrar', exact: true }).click();

    await expect(page.getByRole('heading', { level: 1, name: 'Usuario borrado' })).toBeVisible();
    await expect(page.locator('main header').getByText('Borrado', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Más acciones' })).toHaveCount(0);
    await page.getByRole('link', { name: 'Ver historial' }).click();
    await expect(page).toHaveURL(/entityType=User&entityId=/);
    await expect(page.getByRole('table', { name: /Entradas del registro de auditoría/ })).toContainText(
      'Usuario borrado',
    );

    const context = await browser.newContext({ storageState: { cookies: [], origins: [] }, locale: 'es-ES' });
    const signedOut = await context.newPage();
    await signedOut.goto('/login');
    await signedOut.getByLabel(/^Correo electrónico/).fill(chief.email);
    await signedOut.getByLabel(/^Contraseña/).fill(chief.password);
    await signedOut.getByRole('button', { name: 'Continuar' }).click();
    await expect(signedOut.getByText('El correo o la contraseña no son válidos.')).toBeVisible();
    await context.close();
  });

  test('both pages are in the Admin navigation', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);
    const navigation = await openNavigation(page);

    await expect(navigation.getByRole('link', { name: 'Auditoría' })).toHaveAttribute('href', '/audit-log');
    await expect(navigation.getByRole('link', { name: 'Privacidad' })).toHaveAttribute('href', '/privacy');
  });

  test('a FiringChief has neither page nor navigation entry, and asks for no audit data', async ({
    browser,
  }) => {
    const { context, page } = await asFiringChief(browser);
    const urls = requestUrls(page);
    for (const path of ['/audit-log', '/privacy']) {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeVisible();
    }
    const navigation = await openNavigation(page);
    await expect(navigation.getByRole('link', { name: 'Auditoría' })).toHaveCount(0);
    await expect(navigation.getByRole('link', { name: 'Privacidad' })).toHaveCount(0);
    expect(urls.filter((url) => url.includes('/api/audit-entries') || url.includes('/api/privacy'))).toEqual(
      [],
    );
    await context.close();
  });
});
