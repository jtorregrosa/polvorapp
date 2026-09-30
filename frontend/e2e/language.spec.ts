import { expect, test, waitForShell } from './fixtures';

test.describe('language switching (UC-27)', () => {
  test('switches to Valencian without reloading and remembers it after a reload', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);
    const navigations: string[] = [];
    page.on('framenavigated', (frame) => navigations.push(frame.url()));

    await page.getByRole('combobox', { name: 'Idioma' }).selectOption('ca-ES-valencia');

    await expect(page.getByRole('heading', { level: 1, name: 'Benvinguda' })).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('lang', 'ca-ES-valencia');
    expect(navigations).toEqual([]);

    await page.reload();

    await expect(page.getByRole('heading', { level: 1, name: 'Benvinguda' })).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('lang', 'ca-ES-valencia');
    await expect(page.getByRole('contentinfo')).toContainText(/Versió \d+\.\d+/);
  });

  test('sends the active language to the API', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);
    await page.getByRole('combobox', { name: 'Idioma' }).selectOption('en');

    const [request] = await Promise.all([page.waitForRequest('**/api/system/info'), page.reload()]);

    expect(request.headers()['accept-language']).toBe('en');
  });

  test.describe('with an English browser', () => {
    test.use({ locale: 'en-GB' });

    test('uses the browser language on the first visit', async ({ page }) => {
      await page.goto('/');

      await expect(page.getByRole('heading', { level: 1, name: 'Welcome' })).toBeVisible();
      await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    });
  });

  test.describe('with an unsupported browser language', () => {
    test.use({ locale: 'fr-FR' });

    test('falls back to Spanish', async ({ page }) => {
      await page.goto('/');

      await expect(page.getByRole('heading', { level: 1, name: 'Bienvenida' })).toBeVisible();
      await expect(page.locator('html')).toHaveAttribute('lang', 'es-ES');
    });
  });
});
