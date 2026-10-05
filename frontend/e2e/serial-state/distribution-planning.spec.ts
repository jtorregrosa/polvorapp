import { readFile } from 'node:fs/promises';
import type { Browser, Download, Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from '../identity';
import { expect, test, waitForShell } from '../fixtures';

/**
 * Distribution planning by the Admin (change add-distribution-planning): the Federation logo, the
 * seeded powder day and its slots, and the lists once Norte's order is validated. It changes state
 * other specs read (the seeded day, Norte's order), so it runs in the `serial-state` project and
 * puts everything back as it found it, even when it fails midway. The logo is a synthetic emblem
 * drawn in the browser, never the Federation's (ADR-0012).
 */

const NORTE_ORDER = '0193a700-0000-7000-8000-000000000003';

interface Day {
  id: string;
  type: 'POWDER' | 'WEAPONS';
  date: string;
  location: string;
  version: number;
  slots: { comparsaId: string; startsAt: string }[];
}

interface Plan {
  editionId: string;
  days: Day[];
}

async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

async function currentPlan(page: Page): Promise<Plan> {
  const current = (await (await page.request.get('/api/editions/current')).json()) as {
    edition: { id: string };
  };
  return (await (await page.request.get(`/api/distribution/editions/${current.edition.id}`)).json()) as Plan;
}

const powderOf = (plan: Plan): Day => {
  const day = plan.days.find((candidate) => candidate.type === 'POWDER');
  if (!day) throw new Error('The seed has no powder day');
  return day;
};

async function saved(download: Promise<Download>): Promise<{ name: string; bytes: Buffer }> {
  const file = await download;
  const path = await file.path();
  return { name: file.suggestedFilename(), bytes: await readFile(path) };
}

/** A synthetic emblem: a dark disc on a transparent background. */
async function syntheticEmblem(page: Page): Promise<Buffer> {
  const dataUrl = await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    canvas.width = 600;
    canvas.height = 400;
    const context = canvas.getContext('2d');
    if (!context) throw new Error('No canvas');
    context.fillStyle = '#1c1c1e';
    context.beginPath();
    context.arc(300, 200, 150, 0, Math.PI * 2);
    context.fill();
    return canvas.toDataURL('image/png');
  });
  return Buffer.from(dataUrl.split(',')[1] ?? '', 'base64');
}

async function asFiringChief(browser: Browser) {
  const context = await browser.newContext({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES' });
  return { context, page: await context.newPage() };
}

/**
 * The seed's powder day (DistributionSeeder): four days before the festival, at the synthetic
 * place, with Norte at 09:00 and Sur at 09:30. Fixed values, so a run that failed halfway never
 * becomes the next run's baseline.
 */
const SEEDED_LOCATION = 'Paraje del Reparto';
const SEEDED_SLOTS = [
  { comparsaId: '0193a100-0000-7000-8000-000000000001', startsAt: '09:00' },
  { comparsaId: '0193a100-0000-7000-8000-000000000002', startsAt: '09:30' },
];

async function seededPowderDate(page: Page): Promise<string> {
  const { edition } = (await (await page.request.get('/api/editions/current')).json()) as {
    edition: { year: number; festivalStartsOn: string };
  };
  const date = new Date(`${edition.festivalStartsOn}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() - 4);
  const iso = date.toISOString().slice(0, 10);
  const firstDay = `${edition.year}-01-01`;
  return iso < firstDay ? firstDay : iso;
}

/** Runs every restoration step, even when one fails, and reports all failures at the end. */
async function restoreAll(steps: [string, () => Promise<void>][]): Promise<void> {
  const failures: string[] = [];
  for (const [what, step] of steps) {
    try {
      await step();
    } catch (error) {
      failures.push(`${what}: ${error instanceof Error ? error.message : String(error)}`);
    }
  }
  expect(failures, 'restoring the seeded state after the test').toEqual([]);
}

async function restoreOrder(page: Page, browser: Browser): Promise<void> {
  const order = (await (await page.request.get(`/api/comparsa-orders/${NORTE_ORDER}`)).json()) as {
    status: string;
    version: number;
  };
  if (order.status !== 'VALIDATED') return;
  const returned = await page.request.post(`/api/comparsa-orders/${NORTE_ORDER}/return`, {
    headers: await antiforgeryHeaders(page),
    data: { version: order.version, reason: 'Devuelto por la prueba E2E.' },
  });
  expect(returned.ok(), 'returning the order').toBe(true);
  const { version } = (await (await page.request.get(`/api/comparsa-orders/${NORTE_ORDER}`)).json()) as {
    version: number;
  };
  const { context, page: chief } = await asFiringChief(browser);
  try {
    const submitted = await chief.request.post(`/api/comparsa-orders/${NORTE_ORDER}/submit`, {
      headers: await antiforgeryHeaders(chief),
      data: { version, attestation: true },
    });
    expect(submitted.ok(), 'submitting the order again').toBe(true);
  } finally {
    await context.close();
  }
}

async function restorePowderDay(page: Page): Promise<void> {
  const headers = await antiforgeryHeaders(page);
  const date = await seededPowderDate(page);
  let day = powderOf(await currentPlan(page));
  if (day.date !== date || day.location !== SEEDED_LOCATION) {
    const edited = await page.request.put(`/api/distribution/distributions/${day.id}`, {
      headers,
      data: { date, location: SEEDED_LOCATION, version: day.version },
    });
    expect(edited.ok(), 'restoring the powder day').toBe(true);
    day = powderOf(await currentPlan(page));
  }
  const slots = await page.request.put(`/api/distribution/distributions/${day.id}/slots`, {
    headers,
    data: { version: day.version, slots: SEEDED_SLOTS },
  });
  expect(slots.ok(), 'restoring the powder slots').toBe(true);
}

/** Puts back Norte's submitted order (other specs read it first), the powder day and its slots, and no Federation logo. */
test.afterEach(async ({ page, browser }) => {
  await restoreAll([
    ['order', () => restoreOrder(page, browser)],
    ['powder day', () => restorePowderDay(page)],
    [
      'Federation logo',
      async () => {
        const logo = await page.request.delete('/api/federation-logo', {
          headers: await antiforgeryHeaders(page),
        });
        expect([204, 404]).toContain(logo.status());
      },
    ],
  ]);
});

test('the Admin uploads a Federation logo for the documents and removes it', async ({
  page,
  axeViolations,
}) => {
  await page.goto('/comparsas');
  await waitForShell(page);
  const section = page.getByRole('region', { name: 'Logo de la Federación' });

  const chooser = page.waitForEvent('filechooser');
  await section.getByRole('button', { name: 'Añadir logo de la Federación' }).click();
  await (
    await chooser
  ).setFiles({ name: 'emblema.png', mimeType: 'image/png', buffer: await syntheticEmblem(page) });
  const dialog = page.getByRole('dialog', { name: 'Recortar logo de la Federación' });
  await expect(dialog.getByRole('img', { name: 'Vista previa del logo recortado' })).toBeVisible();
  expect(await axeViolations(page, '[role="dialog"]')).toEqual([]);
  await dialog.getByRole('button', { name: 'Usar logo' }).click();
  await expect(dialog).toBeHidden();
  // The logo is its own button with a menu (refine-navigation-and-lists D7); the image inside is decorative.
  const logo = section.getByRole('button', { name: 'Logo de la Federación, opciones' });
  await expect(logo.locator('img')).toHaveAttribute('src', /^\/api\/federation-logo\?v=[0-9a-f-]{36}$/);
  expect(await axeViolations()).toEqual([]);

  await logo.click();
  await page.getByRole('menuitem', { name: 'Quitar' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Quitar logo' }).click();
  await expect(section.getByRole('button', { name: 'Añadir logo de la Federación' })).toBeVisible();
});

test('a FiringChief cannot upload or remove the Federation logo', async ({ browser }) => {
  const { context, page: chief } = await asFiringChief(browser);
  try {
    const headers = await antiforgeryHeaders(chief);
    const removal = await chief.request.delete('/api/federation-logo', { headers });
    expect(removal.status()).toBe(403);
    const upload = await chief.request.put('/api/federation-logo', {
      headers,
      multipart: {
        file: { name: 'emblema.png', mimeType: 'image/png', buffer: await syntheticEmblem(chief) },
      },
    });
    expect(upload.status()).toBe(403);
  } finally {
    await context.close();
  }
});

test("the Admin changes the powder day's location and Norte's slot", async ({ page }) => {
  await page.goto('/distribution');
  await waitForShell(page);
  const powder = page.getByRole('region', { name: 'Día de reparto de pólvora', exact: true });

  await powder.getByRole('button', { name: 'Editar día de reparto de pólvora' }).click();
  const dayPanel = page.getByRole('dialog', { name: 'Editar el día de pólvora' });
  await dayPanel.getByLabel('Lugar').fill('Paraje Cambiado');
  await dayPanel.getByRole('button', { name: 'Guardar cambios' }).click();
  await expect(dayPanel).toBeHidden();
  await expect(powder).toContainText('Paraje Cambiado');

  await powder.getByRole('button', { name: 'Editar turnos del día de reparto de pólvora' }).click();
  const slotsPanel = page.getByRole('dialog', { name: 'Turnos del día de pólvora' });
  await slotsPanel.getByLabel('Hora de Cruzados (opcional)').fill('08:45');
  await slotsPanel.getByRole('button', { name: 'Guardar cambios' }).click();
  await expect(slotsPanel).toBeHidden();
  await page.reload();
  await waitForShell(page);
  await expect(
    page.getByRole('region', { name: 'Día de reparto de pólvora', exact: true }).getByRole('table'),
  ).toContainText('08:45');
});

test('the Admin downloads the powder list as Excel and PDF once an order is validated', async ({ page }) => {
  const order = (await (await page.request.get(`/api/comparsa-orders/${NORTE_ORDER}`)).json()) as {
    version: number;
  };
  const validated = await page.request.post(`/api/comparsa-orders/${NORTE_ORDER}/validate`, {
    headers: await antiforgeryHeaders(page),
    data: { version: order.version },
  });
  expect(validated.ok(), 'validating Norte for the lists').toBe(true);

  await page.goto('/distribution');
  await waitForShell(page);
  const powder = page.getByRole('region', { name: 'Día de reparto de pólvora', exact: true });
  await expect(powder.getByText('Pedidos sin validar')).toBeVisible();
  await expect(powder).not.toContainText('Cruzados (enviado)');

  const xlsx = page.waitForEvent('download');
  await powder.getByRole('button', { name: 'Descargar el listado de pólvora en Excel' }).click();
  const workbook = await saved(xlsx);
  expect(workbook.name).toMatch(/^polvorapp-\d{4}-powder-distribution-list\.xlsx$/);
  expect(workbook.bytes.subarray(0, 2).toString()).toBe('PK');

  const pdf = page.waitForEvent('download');
  await powder.getByRole('button', { name: 'Descargar el listado de pólvora en PDF' }).click();
  const pdfFile = await saved(pdf);
  expect(pdfFile.name).toMatch(/^polvorapp-\d{4}-powder-distribution-list\.pdf$/);
  expect(pdfFile.bytes.subarray(0, 5).toString()).toBe('%PDF-');
});
