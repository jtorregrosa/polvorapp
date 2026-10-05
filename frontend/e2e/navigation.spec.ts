import type { Page } from '@playwright/test';
import { expect, test, waitForShell } from './fixtures';

/**
 * The grouped navigation and the icon rail (change refine-navigation-and-lists) on the seeded
 * stack, as the Admin. The collapsed state lives in the browser's storage, which every test starts
 * without, so tests never see each other's sidebar.
 */

const sidebar = (page: Page) => page.locator('[data-slot="sidebar"]');
const container = (page: Page) => page.locator('[data-slot="sidebar-container"]');
const navigation = (page: Page) => page.getByRole('navigation', { name: 'Navegación principal' });

test.describe('icon rail on wide screens', () => {
  test.beforeEach(({ isMobile }) => {
    test.skip(isMobile, 'Phones keep the drawer (see below).');
  });

  test('collapses to icons, stays collapsed after a reload and expands with Ctrl+B', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);

    await page.getByRole('button', { name: 'Contraer la navegación' }).click();
    await expect(sidebar(page)).toHaveAttribute('data-state', 'collapsed');
    await expect.poll(async () => (await container(page).boundingBox())?.width).toBe(56);

    await page.reload();
    await waitForShell(page);
    await expect(sidebar(page)).toHaveAttribute('data-state', 'collapsed');
    // Every entry keeps its name, and shows it in a tooltip on keyboard focus.
    await expect(navigation(page).getByRole('link', { name: 'Pedidos' })).toBeVisible();
    await navigation(page).getByRole('link', { name: 'Pedidos' }).focus();
    await expect(page.getByRole('tooltip', { name: 'Pedidos' })).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('tooltip')).toHaveCount(0);

    await page.keyboard.press('Control+b');
    await expect(sidebar(page)).toHaveAttribute('data-state', 'expanded');
    await expect(page.getByRole('button', { name: 'Contraer la navegación' })).toBeVisible();
    await expect(navigation(page).getByRole('group', { name: 'Fiestas' })).toBeVisible();
  });

  test('animates the width over 200 ms, and changes it at once with reduced motion', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);
    const duration = () =>
      container(page).evaluate((element) => getComputedStyle(element).transitionDuration);
    // Without the preference the width moves, so the check below is not vacuous.
    expect(await duration()).toBe('0.2s');

    await page.emulateMedia({ reducedMotion: 'reduce' });
    expect(await duration()).toBe('0s');
    await page.getByRole('button', { name: 'Contraer la navegación' }).click();

    expect((await container(page).boundingBox())?.width).toBe(56);
  });

  for (const [subPage, entry, heading] of [
    ['/orders', 'Pedidos', /^Pedidos/],
    ['/exports', 'Pedidos', /^Exportaciones/],
    ['/distribution', 'Reparto', /^Reparto/],
    ['', 'Ediciones', /^Fiestas/],
  ] as const) {
    test(`marks ${entry} as current on the edition page ${subPage || '(detail)'}`, async ({ page }) => {
      const response = await page.request.get('/api/editions/current');
      expect(response.status(), 'the seed has an edition in progress').toBe(200);
      const { edition } = (await response.json()) as { edition: { id: string } };

      await page.goto(`/editions/${edition.id}${subPage}`);
      await waitForShell(page);
      // The real page, not the not-found page, whose path would match the same entry.
      await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible();

      const current = navigation(page).locator('[aria-current="page"]');
      await expect(current).toHaveCount(1);
      await expect(current).toHaveAccessibleName(entry);
    });
  }

  test('keeps the page usable while the sidebar moves', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);

    await page.keyboard.press('Control+b');
    // Straight away, during the 200 ms: input still goes through (spec: Motion).
    await navigation(page).getByRole('link', { name: 'Pedidos' }).click();

    await expect(page).toHaveURL(/\/orders$/);
    await expect(sidebar(page)).toHaveAttribute('data-state', 'collapsed');
  });

  for (const colorScheme of ['light', 'dark'] as const) {
    test(`has no accessibility violations collapsed, ${colorScheme} theme`, async ({
      page,
      axeViolations,
    }) => {
      await page.emulateMedia({ colorScheme });
      await page.goto('/');
      await waitForShell(page);
      await page.getByRole('button', { name: 'Contraer la navegación' }).click();
      await expect.poll(async () => (await container(page).boundingBox())?.width).toBe(56);

      expect(await axeViolations()).toEqual([]);
    });
  }
});

test.describe('navigation drawer on phones', () => {
  test.beforeEach(({ isMobile }) => {
    test.skip(!isMobile, 'Wide screens get the icon rail (see above).');
  });

  test('opens the full navigation with its sections, never an icon rail', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);

    await page.getByRole('button', { name: 'Mostrar u ocultar la navegación' }).click();

    const drawer = page.getByRole('dialog');
    await expect(drawer.getByRole('group', { name: 'Registro' })).toBeVisible();
    await expect(drawer.getByRole('group', { name: 'Fiestas' })).toBeVisible();
    await expect(drawer.getByText('Fiestas', { exact: true })).toBeVisible();
    await expect(page.locator('[data-collapsible="icon"]')).toHaveCount(0);
  });
});
