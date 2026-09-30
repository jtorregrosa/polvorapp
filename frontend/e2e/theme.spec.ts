import { expect, test } from './fixtures';

test.describe('theme', () => {
  test.use({ colorScheme: 'dark' });

  test('applies the system dark theme before the first paint', async ({ page }) => {
    await page.addInitScript(() => {
      document.addEventListener('DOMContentLoaded', () => {
        document.documentElement.dataset.darkAtDomContentLoaded = String(
          document.documentElement.classList.contains('dark'),
        );
      });
    });

    await page.goto('/');

    await expect(page.locator('html')).toHaveAttribute('data-dark-at-dom-content-loaded', 'true');
    await expect(page.locator('html')).toHaveClass(/dark/);
  });
});
