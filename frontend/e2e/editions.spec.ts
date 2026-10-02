import type { Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from './identity';
import { expect, test, waitForShell } from './fixtures';

/**
 * Festival editions (change add-festival-editions) against the seeded stack: a closed edition last
 * year, the current one in progress with its orders open, and next year's draft. These specs only
 * read the seeded editions; the Admin journey creates its own draft in a far year and deletes it.
 * Moves that change the current edition run in `serial-state/`.
 */

const SEEDED_DRAFT_ID = '0193a500-0000-7000-8000-000000000003';

/** A far year of its own per project, so parallel projects never create the same edition. */
function farYear(): number {
  return test.info().project.name === 'mobile-360' ? 2096 : 2092;
}

async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

/** Deletes a draft of `year` left by an earlier, interrupted run. */
async function removeDraft(page: Page, year: number): Promise<void> {
  const editions = (await (await page.request.get('/api/editions')).json()) as { id: string; year: number }[];
  const leftover = editions.find((edition) => edition.year === year);
  if (leftover) {
    await page.request.delete(`/api/editions/${leftover.id}`, { headers: await antiforgeryHeaders(page) });
  }
}

test.describe('editions as the Admin', () => {
  test('creates a draft, completes it, cannot start it beside the current one, and deletes it', async ({
    page,
  }) => {
    const year = farYear();
    await removeDraft(page, year);

    await page.goto('/editions/new');
    await waitForShell(page);
    await expect(page.getByText(/Se copiarán los precios de la edición \d{4}/)).toBeVisible();
    await page.getByRole('textbox', { name: 'Año' }).fill(String(year));
    await page.getByLabel('Primer día de fiestas').fill(`${String(year)}-04-22`);
    await page.getByLabel('Último día de fiestas').fill(`${String(year)}-04-25`);
    await page.getByRole('button', { name: 'Crear edición' }).click();

    await expect(page.getByRole('heading', { level: 1, name: `Fiestas ${String(year)}` })).toBeVisible();
    await expect(page.getByText(`Edición ${String(year)} creada en preparación.`)).toBeVisible();
    // The prices were copied from the latest earlier edition (the seed's invented 48,00 €).
    await expect(page.getByRole('region', { name: 'Precios' })).toContainText('48,00');

    await page.getByRole('button', { name: 'Editar fechas y plazo de pedidos' }).click();
    const dates = page.getByRole('dialog', { name: 'Editar fechas y plazo de pedidos' });
    await dates.getByLabel('Apertura de pedidos').fill(`${String(year)}-01-10`);
    await dates.getByLabel('Cierre de pedidos').fill(`${String(year)}-02-10`);
    await dates.getByRole('button', { name: 'Guardar cambios' }).click();
    await expect(dates).toBeHidden();
    await expect(page.getByRole('region', { name: 'Fechas y plazo de pedidos' })).toContainText(
      '10 de febrero',
    );

    await page.getByRole('button', { name: 'Añadir hito' }).click();
    const milestone = page.getByRole('dialog', { name: 'Nuevo hito' });
    await milestone.getByLabel('Fecha').fill(`${String(year - 1)}-11-30`);
    await milestone.getByRole('textbox', { name: 'Título' }).fill('Plazo sintético E2E');
    await milestone.getByRole('button', { name: 'Guardar cambios' }).click();
    // A table on wide screens, stacked items on a phone.
    const milestones = page
      .getByRole('table', { name: 'Hitos del calendario' })
      .or(page.getByRole('list', { name: 'Hitos del calendario' }));
    await expect(milestones).toContainText('Plazo sintético E2E');

    // The seeded current edition is in progress: a second one cannot start.
    await page.getByRole('button', { name: 'Iniciar edición' }).click();
    const start = page.getByRole('alertdialog');
    await start.getByRole('button', { name: 'Iniciar edición' }).click();
    await expect(start).toContainText(/ya está en curso/);
    await start.getByRole('button', { name: 'Cancelar' }).click();

    await page.getByRole('button', { name: 'Más acciones' }).click();
    await page.getByRole('menuitem', { name: 'Eliminar edición' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Eliminar edición' }).click();
    await expect(page).toHaveURL(/\/editions$/);
    await expect(page.getByText(`Edición ${String(year)} eliminada.`)).toBeVisible();
  });

  test('the list and an edition have no accessibility violations', async ({ page, axeViolations }) => {
    await page.goto('/editions');
    await waitForShell(page);
    await expect(
      page.getByRole('table', { name: 'Ediciones' }).or(page.getByRole('list')).first(),
    ).toBeVisible();
    expect(await axeViolations()).toEqual([]);

    await page
      .getByRole('link', { name: /^Fiestas \d{4}$/ })
      .first()
      .click();
    await expect(page.getByRole('heading', { level: 2, name: 'Precios' })).toBeVisible();
    expect(await axeViolations()).toEqual([]);
  });
});

test.describe('editions as a FiringChief', () => {
  test.use({ storageState: FIRING_CHIEF_STATE });

  test('sees the current edition read-only, on the editions page and the start page', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);
    const card = page.getByRole('region', { name: 'Edición actual' });
    await expect(card).toContainText('Pedidos abiertos');
    await expect(card).toContainText('Los jefes de disparo pueden editar los pedidos');

    await page.goto('/editions');
    await expect(page.getByText('Edición actual').first()).toBeVisible();
    await expect(page.getByRole('link', { name: 'Nueva edición' })).toHaveCount(0);
    await expect(page.getByText('En preparación')).toHaveCount(0);

    await page
      .getByRole('link', { name: /^Fiestas \d{4}$/ })
      .first()
      .click();
    await expect(page.getByRole('heading', { level: 2, name: 'Precios' })).toBeVisible();
    await expect(page.getByRole('button', { name: /^Editar/ })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Más acciones' })).toHaveCount(0);
  });

  test('gets the not-found page for the seeded draft', async ({ page }) => {
    await page.goto(`/editions/${SEEDED_DRAFT_ID}`);
    await waitForShell(page);

    await expect(page.getByRole('heading', { level: 1, name: 'Página no encontrada' })).toBeVisible();
  });
});
