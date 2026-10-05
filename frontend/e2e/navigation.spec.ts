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
    test.skip(isMobile, 'Phones keep the drawer (checked in layout.spec.ts).');
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

  test("marks Distribution, not Editions, on an edition's distribution page", async ({ page }) => {
    const response = await page.request.get('/api/editions/current');
    test.skip(response.status() !== 200, 'The seed has no edition in progress.');
    const { id } = (await response.json()) as { id: string };

    await page.goto(`/editions/${id}/distribution`);
    await waitForShell(page);

    await expect(navigation(page).getByRole('link', { name: 'Reparto' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expect(navigation(page).getByRole('link', { name: 'Ediciones' })).not.toHaveAttribute(
      'aria-current',
    );
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
