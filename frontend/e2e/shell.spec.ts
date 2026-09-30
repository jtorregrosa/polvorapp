import { expect, test, waitForShell } from './fixtures';

test.describe('application shell', () => {
  test('shows the shell with the API version', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByRole('banner')).toContainText('PolvorApp');
    await expect(page.getByRole('heading', { level: 1, name: 'Bienvenida' })).toBeVisible();
    await expect(page.getByRole('contentinfo')).toContainText(/Versión \d+\.\d+/);
    await expect(page).toHaveTitle('Bienvenida · PolvorApp');
  });

  test('shows the not-found page inside the shell for an unknown route', async ({ page }) => {
    await page.goto('/does/not/exist');

    await expect(page.getByRole('heading', { level: 1, name: 'Página no encontrada' })).toBeVisible();
    await expect(page.getByRole('banner')).toBeVisible();

    await page.getByRole('link', { name: 'Volver al inicio' }).click();

    await expect(page.getByRole('heading', { level: 1, name: 'Bienvenida' })).toBeVisible();
    await expect(page.getByRole('main')).toBeFocused();
  });

  test('offers a skip link that moves focus to the main content', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);

    await page.keyboard.press('Tab');
    const skipLink = page.getByRole('link', { name: 'Saltar al contenido' });
    await expect(skipLink).toBeFocused();
    await expect(skipLink).toBeInViewport();

    await page.keyboard.press('Enter');

    await expect(page.getByRole('main')).toBeFocused();
  });

  test('shows a translated notice when the API is unavailable', async ({ page }) => {
    await page.route('**/api/system/info', (route) => route.abort());

    await page.goto('/');

    await expect(page.getByRole('contentinfo')).toContainText('Versión no disponible');
    await expect(page.getByRole('heading', { level: 1, name: 'Bienvenida' })).toBeVisible();
  });

  test('fits a 360 px screen without horizontal scrolling', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 740 });
    await page.goto('/');
    await waitForShell(page);

    const overflows = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);

    expect(overflows).toBe(false);
  });

  test('has no accessibility violations on the start and not-found pages', async ({
    page,
    axeViolations,
  }) => {
    await page.goto('/');
    await waitForShell(page);
    expect(await axeViolations()).toEqual([]);

    await page.goto('/does/not/exist');
    await waitForShell(page);
    expect(await axeViolations()).toEqual([]);
  });
});
