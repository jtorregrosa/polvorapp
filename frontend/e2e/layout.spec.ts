import { expect, openNavigation, test, waitForShell } from './fixtures';

test.describe('application layout', () => {
  test('shows breadcrumbs and switchers in the top bar', async ({ page }) => {
    await page.goto('/does/not/exist');
    await waitForShell(page);

    const banner = page.getByRole('banner');
    const breadcrumbs = banner.getByRole('navigation', { name: 'Ruta de navegación' });
    await expect(breadcrumbs.getByRole('link', { name: 'Inicio' })).toHaveAttribute('href', '/');
    await expect(breadcrumbs.getByText('Página no encontrada')).toHaveAttribute('aria-current', 'page');
    await expect(banner.getByRole('combobox', { name: 'Idioma' })).toBeVisible();
    await expect(banner.getByRole('button', { name: /^Tema/ })).toBeVisible();
  });

  test('navigates from the navigation and marks the current page', async ({ page }) => {
    await page.goto('/does/not/exist');
    await waitForShell(page);

    const navigation = await openNavigation(page);
    await navigation.getByRole('link', { name: 'Inicio' }).click();

    await expect(page.getByRole('heading', { level: 1, name: 'Bienvenida' })).toBeVisible();
    await expect(page.getByRole('dialog')).toHaveCount(0);
  });
});

test.describe('navigation drawer on small screens', () => {
  test.use({ viewport: { width: 360, height: 740 } });

  test('opens from the top bar, keeps focus inside and closes with Escape', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);
    const trigger = page.getByRole('button', { name: 'Mostrar u ocultar la navegación' });

    await trigger.click();
    const drawer = page.getByRole('dialog');
    await expect(drawer).toBeVisible();
    for (let i = 0; i < 6; i += 1) {
      await page.keyboard.press('Tab');
      await expect(drawer.locator(':focus')).toHaveCount(1);
    }
    await page.keyboard.press('Escape');

    await expect(drawer).toBeHidden();
    await expect(trigger).toBeFocused();
  });
});

test.describe('theme', () => {
  test('remembers an explicit theme across reloads without a flash', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);

    await page.getByRole('button', { name: /^Tema/ }).click();
    await page.getByRole('menuitemradio', { name: 'Oscuro' }).click();
    await expect(page.locator('html')).toHaveClass(/dark/);

    await page.addInitScript(() => {
      document.addEventListener('DOMContentLoaded', () => {
        document.documentElement.dataset.darkAtDomContentLoaded = String(
          document.documentElement.classList.contains('dark'),
        );
      });
    });
    await page.reload();

    await expect(page.locator('html')).toHaveAttribute('data-dark-at-dom-content-loaded', 'true');
  });

  for (const colorScheme of ['light', 'dark'] as const) {
    test.describe(`${colorScheme} theme`, () => {
      test.use({ colorScheme });

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
  }
});
