import { chooseTheme, expect, openNavigation, openUserMenu, test, waitForShell } from './fixtures';

test.describe('application layout', () => {
  test('shows the breadcrumbs and the user menu, which holds the switchers, in the top bar', async ({
    page,
  }) => {
    await page.goto('/does/not/exist');
    await waitForShell(page);

    const banner = page.getByRole('banner');
    const breadcrumbs = banner.getByRole('navigation', { name: 'Ruta de navegación' });
    await expect(breadcrumbs.getByRole('link', { name: 'Inicio' })).toHaveAttribute('href', '/');
    await expect(breadcrumbs.getByText('Página no encontrada')).toHaveAttribute('aria-current', 'page');
    const menu = await openUserMenu(page);
    await expect(menu.getByRole('group', { name: 'Idioma' })).toBeVisible();
    await expect(menu.getByRole('group', { name: 'Tema' })).toBeVisible();
  });

  test('uses the width up to 1680 px beside the sidebar, with the top bar aligned to it', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 2560, height: 1200 });
    await page.goto('/');
    await waitForShell(page);

    const content = await page.getByRole('main').locator('> div').first().boundingBox();
    const bar = await page.getByRole('banner').locator('> div').first().boundingBox();
    const sidebar = await page.locator('[data-slot="sidebar-container"]').boundingBox();
    expect(content?.width).toBe(1680);
    expect(bar?.x).toBe(content?.x);
    expect(bar?.width).toBe(content?.width);
    expect((content?.x ?? 0) - ((sidebar?.x ?? 0) + (sidebar?.width ?? 0))).toBe(28);
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

  test('opens with a short fade and no slide when the user asks for reduced motion', async ({ page }) => {
    // Animations at a hundredth of their speed (Chromium): still running when they are read, while
    // their declared duration is unchanged. Otherwise a 100 ms fade may already be over.
    const cdp = await page.context().newCDPSession(page);
    await cdp.send('Animation.enable');
    await cdp.send('Animation.setPlaybackRate', { playbackRate: 0.01 });
    /** The drawer's opening animation: its duration and whether any keyframe moves it. */
    const openDrawer = async () => {
      await page.goto('/');
      await waitForShell(page);
      await page.getByRole('button', { name: 'Mostrar u ocultar la navegación' }).click();
      return page.getByRole('dialog').evaluate((element) => {
        const [animation] = element.getAnimations();
        if (!animation) return null;
        const keyframes = (animation.effect as KeyframeEffect).getKeyframes();
        return {
          duration: Number(animation.effect?.getComputedTiming().duration),
          moves: keyframes.some((frame) => /translate3d\((?!0px, 0px)/.test(String(frame.transform ?? ''))),
        };
      });
    };

    // Without the preference the drawer slides in, so the check below is not vacuous.
    const normal = await openDrawer();
    expect(normal?.moves).toBe(true);

    await page.emulateMedia({ reducedMotion: 'reduce' });
    const reduced = await openDrawer();
    expect(reduced).not.toBeNull();
    expect(reduced?.moves).toBe(false);
    expect(reduced?.duration).toBeLessThanOrEqual(100);
  });

  test('opens an edit panel with a short fade and no slide when the user asks for reduced motion', async ({
    page,
  }) => {
    // Animations at a hundredth of their speed (Chromium): still running when they are read, while
    // their declared duration is unchanged. Otherwise a 100 ms fade may already be over.
    const cdp = await page.context().newCDPSession(page);
    await cdp.send('Animation.enable');
    await cdp.send('Animation.setPlaybackRate', { playbackRate: 0.01 });
    /** The edit panel's opening animation: its duration and whether any keyframe moves it. */
    const openPanel = async () => {
      await page.goto('/comparsas/0193a100-0000-7000-8000-000000000001');
      await waitForShell(page);
      await page.getByRole('button', { name: 'Editar datos de la comparsa' }).click();
      return page.getByRole('dialog', { name: 'Editar datos de la comparsa' }).evaluate((element) => {
        const [animation] = element.getAnimations();
        if (!animation) return null;
        const keyframes = (animation.effect as KeyframeEffect).getKeyframes();
        return {
          duration: Number(animation.effect?.getComputedTiming().duration),
          moves: keyframes.some((frame) => /translate3d\((?!0px, 0px)/.test(String(frame.transform ?? ''))),
        };
      });
    };

    // Without the preference the panel slides in, so the check below is not vacuous.
    const normal = await openPanel();
    expect(normal?.moves).toBe(true);

    await page.emulateMedia({ reducedMotion: 'reduce' });
    const reduced = await openPanel();
    expect(reduced).not.toBeNull();
    expect(reduced?.moves).toBe(false);
    expect(reduced?.duration).toBeLessThanOrEqual(100);
  });
});

test.describe('theme', () => {
  test('remembers an explicit theme across reloads without a flash', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);

    await chooseTheme(page, 'Oscuro');
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
