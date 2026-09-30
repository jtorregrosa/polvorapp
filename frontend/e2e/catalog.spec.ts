import { randomUUID } from 'node:crypto';
import type { Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from './identity';
import { expect, openNavigation, test as base, waitForShell } from './fixtures';

/**
 * Federation catalogue (change add-federation-catalog) against the seeded stack: fictional
 * comparsas, the seeded FiringChiefs and the synthetic weapon catalogue (docs/development.md).
 * Each test creates what it changes, with a unique name, and removes it again, so the desktop and
 * mobile projects can run side by side. The Admin flows assign "Jefe Sintético Uno", never the
 * signed-in FiringChief of the FiringChief tests ("Jefa Sintética Dos"), whose scope stays fixed.
 */

const SEEDED_SUR = '0193a100-0000-7000-8000-000000000002';
const SEEDED_NORTE = '0193a100-0000-7000-8000-000000000001';
const JEFE_UNO = '0193a000-0000-7000-8000-000000000002';

const unique = (prefix: string): string => `${prefix} ${randomUUID().slice(0, 8)}`;

/** Headers for a state-changing API call made directly by a test, as the UI sends them. */
async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

const test = base.extend<{
  /** Deletes what a test created through the API afterwards, even when the test failed midway. */
  deleteAfter: (resource: string) => void;
}>({
  deleteAfter: async ({ page }, use) => {
    const resources: string[] = [];
    await use((resource) => resources.push(resource));
    if (resources.length === 0) return;
    const headers = await antiforgeryHeaders(page);
    for (const resource of resources) {
      // A 404 means the test already deleted it; anything else means the cleanup itself is broken.
      const response = await page.request.delete(resource, { headers });
      expect([204, 404], `cleanup of ${resource}`).toContain(response.status());
    }
  },
});

/** The id at the end of the current URL, e.g. of a detail page just created. */
const idFromUrl = (page: Page): string => new URL(page.url()).pathname.split('/').pop() ?? '';

const notice = (scope: Page | ReturnType<Page['getByRole']>, text: string) =>
  scope.getByRole('status').filter({ hasText: text });

async function expectNoHorizontalOverflow(page: Page): Promise<void> {
  const overflows = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  expect(overflows).toBe(false);
}

test.describe('federation catalogue as an Admin', () => {
  test('creates a comparsa, assigns a FiringChief from it, removes them from the user page and deletes the comparsa', async ({
    page,
    deleteAfter,
  }) => {
    // Two full reloads and three dialogs.
    test.setTimeout(60_000);
    const name = unique('Comparsa E2E');

    await page.goto('/comparsas');
    await waitForShell(page);
    // Positive control for the FiringChief test, which expects this entry to be missing.
    await expect((await openNavigation(page)).getByRole('link', { name: 'Modelos de arma' })).toBeVisible();
    await page.keyboard.press('Escape');
    await page.getByRole('link', { name: 'Nueva comparsa' }).click();
    await page.getByRole('textbox', { name: /Nombre/ }).fill(name);
    await page.getByRole('combobox', { name: /Bando/ }).selectOption('MOORISH');
    await page.getByRole('button', { name: 'Crear comparsa' }).click();
    await expect(page.getByRole('heading', { level: 1, name })).toBeVisible();
    const comparsaId = idFromUrl(page);
    deleteAfter(`/api/comparsas/${comparsaId}`);
    await expect(notice(page, 'Comparsa creada.')).toBeFocused();

    const chiefs = page.getByRole('group', { name: 'Jefes de disparo', exact: true });
    await chiefs
      .getByRole('combobox', { name: /Jefe de disparo que añadir/ })
      .selectOption({ label: 'Jefe Sintético Uno (jefe.uno@polvorapp.example)' });
    await chiefs.getByRole('button', { name: 'Añadir' }).click();
    await expect(notice(chiefs, 'Jefe Sintético Uno ya es jefe de disparo de esta comparsa.')).toBeFocused();
    await expect(chiefs.getByRole('table', { name: 'Jefes de disparo' })).toContainText(
      'jefe.uno@polvorapp.example',
    );
    await expect(page.getByRole('region', { name: 'Jefes de disparo' })).toBeVisible();

    // The same assignment, seen and removed from the other side: the FiringChief's user page.
    await page.goto(`/users/${JEFE_UNO}`);
    await waitForShell(page);
    const comparsas = page.getByRole('group', { name: 'Comparsas', exact: true });
    await expect(comparsas.getByRole('table', { name: 'Comparsas' })).toContainText(name);
    await expect(page.getByRole('region', { name: 'Comparsas' })).toBeVisible();
    await expectNoHorizontalOverflow(page);
    await comparsas.getByRole('button', { name: `Quitar ${name}` }).click();
    await page
      .getByRole('alertdialog')
      .getByRole('button', { name: `Quitar ${name}` })
      .click();
    await expect(notice(comparsas, `Comparsa ${name} quitada.`)).toBeFocused();
    await expect(comparsas.getByRole('table', { name: 'Comparsas' })).not.toContainText(name);

    await page.goto(`/comparsas/${comparsaId}`);
    await page.getByRole('button', { name: 'Eliminar comparsa' }).click();
    const dialog = page.getByRole('alertdialog', { name: `¿Eliminar ${name}?` });
    await expect(dialog).toContainText('No se puede deshacer.');
    await dialog.getByRole('button', { name: 'Eliminar comparsa' }).click();
    await expect(notice(page, 'Comparsa eliminada.')).toBeFocused();
    await expect(page).toHaveURL(/\/comparsas$/);
    // The list has loaded before the deleted row is looked for.
    await expect(page.getByRole('link', { name: 'Comparsa Sintética Norte' })).toBeVisible();
    await expect(page.getByRole('link', { name })).toHaveCount(0);
  });

  test('creates a pistol, which can never be rented, and deletes it', async ({ page, deleteAfter }) => {
    const label = unique('PISTOLA E2E');

    await page.goto('/weapon-models/new');
    await waitForShell(page);
    await page.getByRole('combobox', { name: /Tipo/ }).selectOption('TRABUCO');
    const rentable = page.getByRole('checkbox', { name: 'Se puede alquilar' });
    await rentable.check();
    await page.getByRole('combobox', { name: /Tipo/ }).selectOption('PISTOL');
    await expect(rentable).toBeDisabled();
    await expect(rentable).not.toBeChecked();
    await page.getByRole('textbox', { name: /Nombre/ }).fill(label);
    await page.getByRole('button', { name: 'Crear modelo' }).click();

    await expect(page.getByRole('heading', { level: 1, name: label })).toBeVisible();
    deleteAfter(`/api/weapon-models/${idFromUrl(page)}`);
    await expect(page.getByRole('checkbox', { name: 'Se puede alquilar' })).not.toBeChecked();

    await page.getByRole('button', { name: 'Eliminar modelo' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Eliminar modelo' }).click();
    await expect(notice(page, 'Modelo de arma eliminado.')).toBeFocused();
    // The list has loaded before the deleted row is looked for.
    await expect(page.getByRole('link', { name: 'PISTOLA', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: label })).toHaveCount(0);
  });
});

test.describe('federation catalogue as a FiringChief', () => {
  test.use({ storageState: FIRING_CHIEF_STATE });

  test('sees only their own comparsa, gets not-found for another one and has no weapon catalogue', async ({
    page,
  }) => {
    await page.goto('/comparsas');
    await waitForShell(page);

    const table = page.getByRole('table', { name: 'Comparsas' });
    await expect(table.getByRole('link', { name: 'Comparsa Sintética Norte' })).toBeVisible();
    // The seed assigns Jefa Sintética Dos to Norte only (docs/development.md).
    await expect(table.getByRole('link')).toHaveCount(1);
    await expect(page.getByRole('link', { name: 'Nueva comparsa' })).toHaveCount(0);

    const navigation = await openNavigation(page);
    await expect(navigation.getByRole('link', { name: 'Comparsas' })).toBeVisible();
    await expect(navigation.getByRole('link', { name: 'Modelos de arma' })).toHaveCount(0);

    await page.goto(`/comparsas/${SEEDED_SUR}`);
    await expect(page.getByRole('heading', { level: 1, name: 'Página no encontrada' })).toBeVisible();
    // Enforced by the server, not only hidden by the UI (BR-12).
    expect((await page.request.get(`/api/comparsas/${SEEDED_SUR}`)).status()).toBe(404);
    // The catalogue is readable by a FiringChief (spec: Weapon catalogue access); changing it is not.
    const change = await page.request.post('/api/weapon-models', {
      headers: await antiforgeryHeaders(page),
      data: { kind: 'PISTOL', rentable: false, label: unique('PISTOLA E2E') },
    });
    expect(change.status()).toBe(403);

    await page.goto(`/comparsas/${SEEDED_NORTE}`);
    await expect(page.getByRole('heading', { level: 1, name: 'Comparsa Sintética Norte' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Eliminar comparsa' })).toHaveCount(0);
  });
});

test.describe('accessibility of the catalogue pages', () => {
  for (const colorScheme of ['light', 'dark'] as const) {
    test.describe(`${colorScheme} theme`, () => {
      test.use({ colorScheme });

      test('comparsa list', async ({ page, axeViolations }) => {
        await page.goto('/comparsas');
        await waitForShell(page);
        await expect(page.getByRole('link', { name: 'Comparsa Sintética Norte' })).toBeVisible();
        await expectNoHorizontalOverflow(page);
        expect(await axeViolations()).toEqual([]);
      });

      test('comparsa detail with its FiringChiefs and the add control', async ({ page, axeViolations }) => {
        // Sur always has a candidate (Jefa Sintética Dos), so the add control is scanned too.
        await page.goto(`/comparsas/${SEEDED_SUR}`);
        await waitForShell(page);
        await expect(page.getByRole('table', { name: 'Jefes de disparo' })).toBeVisible();
        await expect(page.getByRole('combobox', { name: /Jefe de disparo que añadir/ })).toBeVisible();
        await expectNoHorizontalOverflow(page);
        expect(await axeViolations()).toEqual([]);
      });

      test('user page with the comparsas of a FiringChief', async ({ page, axeViolations }) => {
        await page.goto(`/users/${JEFE_UNO}`);
        await waitForShell(page);
        await expect(page.getByRole('table', { name: 'Comparsas' })).toBeVisible();
        await expect(page.getByRole('combobox', { name: /Comparsa que añadir/ })).toBeVisible();
        await expectNoHorizontalOverflow(page);
        expect(await axeViolations()).toEqual([]);
      });

      test('weapon models', async ({ page, axeViolations }) => {
        await page.goto('/weapon-models?includeInactive=true');
        await waitForShell(page);
        await expect(page.getByRole('link', { name: 'PISTOLA', exact: true })).toBeVisible();
        await expectNoHorizontalOverflow(page);
        expect(await axeViolations()).toEqual([]);
      });

      test('weapon model form', async ({ page, axeViolations }) => {
        await page.goto('/weapon-models/new');
        await waitForShell(page);
        await expect(page.getByRole('combobox', { name: /Tipo/ })).toBeVisible();
        await expectNoHorizontalOverflow(page);
        expect(await axeViolations()).toEqual([]);
      });
    });
  }
});
