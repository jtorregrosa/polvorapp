import type { Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from './identity';
import { expect, openNavigation, test as base, waitForShell } from './fixtures';

/**
 * Compliance insights (change add-compliance-insights) against the seeded stack. The seeded
 * FiringChief "Elena Verdú Ivorra" sees Cruzados, where "Laia Sempere
 * Pastor" is 16, without the course or an ID photo, and with a license expiring within 12 months.
 * Seeded data is only read; a test that changes data registers its own arquebusier and deletes it.
 */

const NORTE_ID = '0193a100-0000-7000-8000-000000000001';
const SUR_ID = '0193a100-0000-7000-8000-000000000002';
const CATORCE_ID = '0193a300-0000-7000-8000-000000000014';
const LETTERS = 'TRWAGMYFPDXBNJZSQVHLCKE';

let identities = 0;

/**
 * A synthetic identity unique across workers and runs, in a range of its own: DNI numbers and
 * federation ids from 40M (the seed uses 1–99, the registry spec up to 9M, the photos spec 20M–29M).
 */
function syntheticIdentity(): { nationalId: string; federationId: number; lastName: string } {
  const second = Math.floor(Date.now() / 1000) % 100_000;
  identities += 1;
  const number = 40_000_000 + ((second * 90 + test.info().parallelIndex * 9 + (identities % 9)) % 9_000_000);
  return {
    nationalId: `${String(number).padStart(8, '0')}${LETTERS[number % LETTERS.length]}`,
    federationId: number,
    lastName: `Prueba Avisos ${number}`,
  };
}

async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

/** The ISO date `years` years and 40 days before today, so an age stays the same during the test. */
function dateYearsAgo(years: number): string {
  const date = new Date();
  date.setUTCFullYear(date.getUTCFullYear() - years);
  date.setUTCDate(date.getUTCDate() - 40);
  return date.toISOString().slice(0, 10);
}

const test = base.extend<{
  /** Registers a 16-year-old without the course in Norte through the API, and deletes it afterwards. */
  minor: { id: string; name: string };
}>({
  minor: async ({ page }, use) => {
    const identity = syntheticIdentity();
    const headers = await antiforgeryHeaders(page);
    const registered = await page.request.post('/api/arquebusiers', {
      headers,
      data: {
        comparsaId: NORTE_ID,
        federationId: identity.federationId,
        nationalId: identity.nationalId,
        firstName: 'Arcabucera',
        lastName: identity.lastName,
        birthDate: dateYearsAgo(16),
        email: null,
        phone: null,
        gender: 'FEMALE',
        status: 'ACTIVE',
        trainingCompletedOn: null,
        license: null,
      },
    });
    expect(registered.status()).toBe(201);
    const { id } = (await registered.json()) as { id: string };
    await use({ id, name: `Arcabucera ${identity.lastName}` });
    const deleted = await page.request.delete(`/api/arquebusiers/${id}`, {
      headers: await antiforgeryHeaders(page),
    });
    expect([204, 404]).toContain(deleted.status());
  },
});

const region = (page: Page, name: string) => page.getByRole('region', { name, exact: true });

test.describe('as the seeded FiringChief', () => {
  test.use({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES' });

  test('the start page shows the figures and the next expiries of her comparsa only', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);

    await expect(region(page, 'Arcabuceros').getByRole('link', { name: /Con avisos/ })).toHaveAttribute(
      'href',
      '/arquebusiers?warning=ANY',
    );
    await expect(region(page, 'Avisos').getByRole('link')).toHaveCount(8);
    const expiries = region(page, 'Próximas caducidades');
    await expect(expiries.getByRole('link', { name: 'Sempere Pastor, Laia' })).toBeVisible();
    // Arquebusiers of other comparsas never reach her dashboard.
    await expect(expiries.getByText('Abencerrajes')).toHaveCount(0);
    // The navigation says how many of her arquebusiers have warnings (a drawer on phones).
    const navigation = await openNavigation(page);
    await expect(navigation.getByRole('link', { name: /^Arcabuceros, \d+ con avisos?$/ })).toBeVisible();
  });

  test('a warning figure opens the list filtered by that warning', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);

    await region(page, 'Avisos')
      .getByRole('link', { name: /Sin curso/ })
      .click();

    await expect(page).toHaveURL('/arquebusiers?warning=COURSE_MISSING');
    await waitForShell(page);
    const list = page
      .getByRole('table', { name: 'Arcabuceros' })
      .or(page.getByRole('list', { name: 'Arcabuceros' }));
    await expect(list.getByRole('link', { name: 'Sempere Pastor, Laia' })).toBeVisible();
    // "Vicent Sempere Llorens" has the course done; "Toni Baeza Ripoll" has none but is
    // in Abencerrajes, outside her scope (BR-12).
    await expect(list.getByRole('link', { name: 'Sempere Llorens, Vicent' })).toHaveCount(0);
    await expect(list.getByRole('link', { name: 'Baeza Ripoll, Toni' })).toHaveCount(0);
  });

  test('the detail of a seeded minor lists her four warnings and the license as expiring soon', async ({
    page,
  }) => {
    await page.goto(`/arquebusiers/${CATORCE_ID}`);
    await waitForShell(page);

    const banner = page.locator('[data-severity="warning"]').filter({ hasText: 'Avisos de cumplimiento' });
    const items = banner.getByRole('listitem');
    await expect(items).toHaveCount(4);
    await expect(items.nth(0)).toContainText('en menos de 12 meses');
    await expect(items.nth(1)).toHaveText('No consta el curso de arcabucería.');
    await expect(items.nth(2)).toHaveText('Es menor de edad: tiene 16 años.');
    await expect(items.nth(3)).toContainText('Falta la foto de carnet');
    await expect(page.locator('main header')).toContainText('Caduca pronto');
  });

  test('recording the course removes its warning', async ({ page, minor }) => {
    await page.goto(`/arquebusiers/${minor.id}`);
    await waitForShell(page);
    const banner = page.locator('[data-severity="warning"]').filter({ hasText: 'Avisos de cumplimiento' });
    await expect(banner).toContainText('No consta el curso de arcabucería.');

    await page.getByRole('button', { name: 'Editar curso' }).click();
    const panel = page.getByRole('dialog', { name: 'Editar curso' });
    await panel.getByLabel(/Fecha del curso/).fill(dateYearsAgo(0));
    // Saving refetches the warning summary behind the navigation count and the dashboard.
    const summaryRefetched = page.waitForResponse(
      (response) =>
        response.url().endsWith('/api/compliance/summary') && response.request().method() === 'GET',
    );
    await panel.getByRole('button', { name: 'Guardar cambios' }).click();
    await expect(panel).toBeHidden();
    expect((await summaryRefetched).ok()).toBe(true);

    await expect(banner).not.toContainText('No consta el curso de arcabucería.');
    await expect(banner).toContainText('Es menor de edad: tiene 16 años.');
  });
});

test.describe('as the Admin', () => {
  test('the statistics keep the comparsa filter after a reload', async ({ page }) => {
    await page.goto('/statistics');
    await waitForShell(page);
    await expect(page.getByRole('table', { name: 'Arcabuceros por género' })).toBeVisible();

    await page.getByRole('combobox', { name: 'Comparsa' }).selectOption({ label: 'Abencerrajes' });
    await expect(page).toHaveURL(`/statistics?comparsaId=${SUR_ID}`);
    await page.reload();
    await waitForShell(page);

    await expect(page.getByRole('combobox', { name: 'Comparsa' })).toHaveValue(SUR_ID);
    await expect(page.getByRole('status').getByText(/^Mostrando \d+ arcabucer/)).toBeVisible();
    // Filtered to one comparsa: no per-comparsa table.
    await expect(page.getByRole('table', { name: 'Arcabuceros por género' })).toBeVisible();
    await expect(page.getByRole('table', { name: 'Cifras por comparsa' })).toHaveCount(0);
  });
});

test.describe('trends (change add-statistics-trends)', () => {
  test('an Admin opens "Trends", sees the arquebusiers chart and opens its table', async ({ page }) => {
    await page.goto('/statistics');
    await waitForShell(page);
    await page.getByRole('tab', { name: 'Tendencias' }).click();
    await expect(page).toHaveURL('/statistics?view=trends');

    const chart = region(page, 'Arcabuceros por edición');
    await expect(chart.getByRole('application', { name: 'Arcabuceros por edición' })).toBeVisible();
    await expect(chart).toContainText(/\(provisional\): \d+ en activo/);
    await chart.getByRole('button', { name: 'Tabla de datos: Arcabuceros por edición' }).click();

    const table = page.getByRole('table', { name: 'Arcabuceros por edición: datos' });
    await expect(table.getByRole('rowheader')).toHaveCount(2);
    await expect(table.getByRole('rowheader').last()).toHaveText(/^\d{4} \(provisional\)$/);
    await expect(
      page.getByRole('table', { name: 'Arcabuceros en activo por comparsa y edición' }),
    ).toBeVisible();
  });

  test('the keyboard moves through the editions and the values are said', async ({ page }) => {
    await page.goto('/statistics?view=trends');
    await waitForShell(page);
    const chart = region(page, 'Arcabuceros por edición');
    const drawing = chart.getByRole('application', { name: 'Arcabuceros por edición' });

    await drawing.focus();
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('ArrowRight');

    await expect(chart.getByRole('status')).toContainText(
      /^\d{4} \(provisional\): En activo \d+ y En reserva \d+$/,
    );
    await expect(chart.locator('.recharts-tooltip-wrapper')).toContainText('(provisional)');
  });
});

test.describe('trends as the seeded FiringChief', () => {
  test.use({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES' });

  test('only her comparsa is counted, without the per-comparsa table', async ({ page }) => {
    await page.goto('/statistics?view=trends');
    await waitForShell(page);

    await expect(region(page, 'Arcabuceros por edición')).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Comparsa' })).toHaveCount(0);
    await expect(page.getByRole('heading', { name: 'Arcabuceros en activo por comparsa' })).toHaveCount(0);
  });
});

for (const colorScheme of ['light', 'dark'] as const) {
  test(`the start page, the statistics and the trends pass axe and fit the screen in the ${colorScheme} theme`, async ({
    page,
    axeViolations,
  }) => {
    await page.emulateMedia({ colorScheme });
    // A content marker per page, so axe and the overflow check never run on a skeleton.
    const loaded: Record<string, (page: Page) => ReturnType<Page['getByRole']>> = {
      '/': (current) => region(current, 'Avisos').getByRole('link').first(),
      '/statistics': (current) => current.getByRole('table', { name: 'Arcabuceros por género' }),
      '/statistics?view=trends': (current) =>
        current.getByRole('table', { name: 'Arcabuceros en activo por comparsa y edición' }),
    };
    for (const [path, marker] of Object.entries(loaded)) {
      await page.goto(path);
      await waitForShell(page);
      await expect(marker(page)).toBeVisible();
      await expect(page.locator('html')).toHaveClass(colorScheme === 'dark' ? /\bdark\b/ : /^(?!.*\bdark\b)/);
      const overflows = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      expect(overflows, path).toBe(false);
      expect(await axeViolations(), path).toEqual([]);
    }
  });
}
