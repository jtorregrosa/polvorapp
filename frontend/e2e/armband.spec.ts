import type { Locator, Page } from '@playwright/test';
import { chooseLanguage, expect, openNavigation, test, waitForShell } from './fixtures';
import { ADMIN_STATE, FIRING_CHIEF_STATE } from './identity';

/**
 * The FiringChief armband (change add-firing-chief-armband) on the seeded stack. Other specs change
 * the seeded users' language, so names are matched in any of the three.
 */

const ROLE_LABEL = /^(Jefe de disparo|Cap de disparada|Firing chief)$/;
const COLLAPSE = /^(Contraer la navegación|Replega la navegació|Collapse navigation)$/;
const NAVIGATION = /^(Navegación principal|Navegació principal|Main navigation)$/;

const armband = (page: Page) => page.locator('[data-slot="sidebar-armband"]');
const container = (page: Page) => page.locator('[data-slot="sidebar-container"]');
/** The night surface inside the container's border: what "the full width of the sidebar" is. */
const surface = (page: Page) => page.locator('[data-slot="sidebar-inner"]');

async function box(locator: Locator) {
  const found = await locator.boundingBox();
  expect(found, 'element has a box').not.toBeNull();
  return found ?? { x: 0, y: 0, width: 0, height: 0 };
}

/** Waits until no transition runs, e.g. the sidebar's 200 ms width and the labels' fade. */
async function settle(page: Page): Promise<void> {
  await page.waitForFunction(() =>
    document.getAnimations().every((animation) => animation.playState !== 'running'),
  );
}

async function collapse(page: Page): Promise<void> {
  await page.getByRole('button', { name: COLLAPSE }).click();
  await expect.poll(async () => (await container(page).boundingBox())?.width).toBe(56);
  await settle(page);
}

test.describe('armband on wide screens', () => {
  test.use({ storageState: FIRING_CHIEF_STATE });

  test.beforeEach(({ isMobile }) => {
    test.skip(isMobile, 'Phones show it in the drawer (see below).');
  });

  test('shows the role in capitals in a yellow band across the bottom of the sidebar', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);

    await expect(armband(page)).toHaveText(ROLE_LABEL);
    await expect(armband(page)).toHaveCSS('text-transform', 'uppercase');
    await expect(armband(page)).toHaveCSS('background-color', 'rgb(245, 197, 24)');
    const band = await box(armband(page));
    const sidebar = await box(surface(page));
    expect(band.x).toBeCloseTo(sidebar.x, 0);
    expect(band.width).toBeCloseTo(sidebar.width, 0);
  });

  test('stays in view while the navigation scrolls on a short screen', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 420 });
    await page.goto('/');
    await waitForShell(page);
    const content = page.locator('[data-slot="sidebar-content"]');
    expect(await content.evaluate((element) => element.scrollHeight > element.clientHeight)).toBe(true);

    await content.evaluate((element) => {
      element.scrollTop = element.scrollHeight;
    });

    await expect(armband(page)).toBeInViewport({ ratio: 1 });
  });

  test('becomes a stripe without text in the same place in the icon rail, with no tooltip', async ({
    page,
  }) => {
    await page.goto('/');
    await waitForShell(page);
    const expanded = await box(armband(page));

    await collapse(page);

    await expect(armband(page)).toHaveAttribute('aria-hidden', 'true');
    await expect(armband(page).locator('[data-sidebar-label]')).toHaveCSS('opacity', '0');
    const rail = await box(armband(page));
    expect(rail.y).toBeCloseTo(expanded.y, 0);
    expect(rail.height).toBeCloseTo(expanded.height, 0);
    expect(rail.width).toBeCloseTo((await box(surface(page))).width, 0);
    // Rail entries do show a tooltip on hover; the armband right after it shows none.
    await page.getByRole('navigation', { name: NAVIGATION }).getByRole('link').first().hover();
    await expect(page.getByRole('tooltip')).toBeVisible();
    // A real pointer passes through the rail on its way down: step it, so Radix's grace area closes.
    const stripe = await box(armband(page));
    await page.mouse.move(stripe.x + stripe.width / 2, stripe.y + stripe.height / 2, { steps: 20 });
    await expect(page.getByRole('tooltip')).toHaveCount(0);
  });

  test('shows the whole Valencian label with WCAG 1.4.12 text spacing, wrapping instead of clipping', async ({
    page,
  }) => {
    await page.goto('/');
    await waitForShell(page);
    await chooseLanguage(page, 'Valencià');
    try {
      await expect(armband(page)).toHaveText('Cap de disparada');

      await page.addStyleTag({
        content:
          '* { line-height: 1.5 !important; letter-spacing: 0.12em !important; word-spacing: 0.16em !important; } p { margin-bottom: 2em !important; }',
      });

      const clipped = await armband(page).evaluate((band) =>
        [band, band.querySelector('[data-sidebar-label]')].some(
          (element) =>
            !element ||
            element.scrollWidth > element.clientWidth ||
            element.scrollHeight > element.clientHeight,
        ),
      );
      expect(clipped).toBe(false);
    } finally {
      // The language is saved for the seeded user: leave it as the other specs expect.
      await chooseLanguage(page, 'Español');
    }
  });

  for (const colorScheme of ['light', 'dark'] as const) {
    test(`has no accessibility violations, expanded and collapsed, ${colorScheme} theme`, async ({
      page,
      axeViolations,
    }) => {
      await page.emulateMedia({ colorScheme });
      await page.goto('/');
      await waitForShell(page);
      expect(await axeViolations()).toEqual([]);

      await collapse(page);

      expect(await axeViolations()).toEqual([]);
    });
  }
});

test.describe('armband on phones', () => {
  test.use({ storageState: FIRING_CHIEF_STATE });

  test.beforeEach(({ isMobile }) => {
    test.skip(!isMobile, 'Wide screens show it at the bottom of the sidebar (see above).');
  });

  test('shows the armband in the drawer, and none in the bottom bar', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);
    const bar = page.locator('[data-slot="bottom-nav"]');
    await expect(bar).toBeVisible();
    await expect(bar).not.toContainText(ROLE_LABEL);

    const drawer = await openNavigation(page);

    const band = page.getByRole('dialog').locator('[data-slot="sidebar-armband"]');
    await expect(drawer).toBeVisible();
    await expect(band).toHaveText(ROLE_LABEL);
    await expect(band).not.toHaveAttribute('aria-hidden');
  });
});

test.describe('no armband for Admins', () => {
  test.use({ storageState: ADMIN_STATE });

  test('shows the sidebar without an armband', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);

    await expect(await openNavigation(page)).toBeVisible();
    await expect(armband(page)).toHaveCount(0);
  });
});
