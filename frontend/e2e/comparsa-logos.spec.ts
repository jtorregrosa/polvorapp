import { randomUUID } from 'node:crypto';
import type { Page } from '@playwright/test';
import { ADMIN_STATE, FIRING_CHIEF_UNO_STATE } from './identity';
import { expect, test as base, waitForShell } from './fixtures';

/**
 * Comparsa logos (change add-comparsa-logos) against the seeded stack. The Admin tests create the
 * comparsa they change and delete it again; the logo is a synthetic emblem drawn in the browser,
 * never a real one (ADR-0012). "Joan Moltó Sala" is assigned to Norte (seeded with a logo) and
 * Sur (seeded without one). The stored PNG is read back and decoded by the engine under test.
 */

const NORTE = '0193a100-0000-7000-8000-000000000001';
const SUR = '0193a100-0000-7000-8000-000000000002';
/** Seeded with a logo and not assigned to Joan Moltó Sala. */
const ESTE = '0193a100-0000-7000-8000-000000000003';

async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

const test = base.extend<{ newComparsa: () => Promise<{ id: string; name: string }> }>({
  newComparsa: async ({ page }, use) => {
    const ids: string[] = [];
    await use(async () => {
      const name = `Comparsa Prueba Logo ${randomUUID().slice(0, 8)}`;
      const response = await page.request.post('/api/comparsas', {
        headers: await antiforgeryHeaders(page),
        data: { name, side: 'MOORISH' },
      });
      expect(response.status()).toBe(201);
      const { id } = (await response.json()) as { id: string };
      ids.push(id);
      return { id, name };
    });
    const headers = await antiforgeryHeaders(page);
    for (const id of ids) {
      const deletion = await page.request.delete(`/api/comparsas/${id}`, { headers });
      expect([204, 404], 'cleanup of the created comparsa').toContain(deletion.status());
    }
  },
});

/** A synthetic emblem: a dark disc on the left half and a fully transparent background. */
async function transparentEmblem(page: Page): Promise<Buffer> {
  const dataUrl = await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    canvas.width = 600;
    canvas.height = 400;
    const context = canvas.getContext('2d');
    if (!context) throw new Error('No canvas');
    context.fillStyle = '#1c1c1e';
    context.beginPath();
    context.arc(150, 200, 120, 0, Math.PI * 2);
    context.fill();
    return canvas.toDataURL('image/png');
  });
  return Buffer.from(dataUrl.split(',')[1] ?? '', 'base64');
}

/** The alpha of the stored logo at each point, decoded by the browser under test. */
async function alphaAt(page: Page, url: string, points: [number, number][]): Promise<number[]> {
  return page.evaluate(
    async ({ url, points }) => {
      const bitmap = await createImageBitmap(await (await fetch(url)).blob());
      const canvas = document.createElement('canvas');
      canvas.width = bitmap.width;
      canvas.height = bitmap.height;
      const context = canvas.getContext('2d');
      if (!context) throw new Error('No canvas');
      context.drawImage(bitmap, 0, 0);
      return points.map(([x, y]) => context.getImageData(x, y, 1, 1).data[3] ?? -1);
    },
    { url, points },
  );
}

/** The logo tile in the page's record header (the only `ComparsaLogo` in the main content). */
const headerLogo = (page: Page) => page.getByRole('main').locator('[data-slot="comparsa-logo"]');

/** Opens the navigation drawer on phones, where the sidebar (and its cards) is hidden. */
async function showSidebar(page: Page): Promise<void> {
  if ((page.viewportSize()?.width ?? 1280) < 768) {
    await page.getByRole('button', { name: 'Mostrar u ocultar la navegación' }).click();
    await expect(page.getByRole('dialog')).toBeVisible();
  }
}

test.describe('comparsa logos', () => {
  test('an Admin replaces a logo from its own menu with the keyboard, and focus returns to it', async ({
    page,
    newComparsa,
  }) => {
    const { id, name } = await newComparsa();
    await page.goto(`/comparsas/${id}`);
    await waitForShell(page);
    // Add one first, from the placeholder, which opens the file chooser at once.
    const chooser = page.waitForEvent('filechooser');
    await page.getByRole('button', { name: 'Añadir logo de la comparsa' }).click();
    await (
      await chooser
    ).setFiles({ name: 'emblema.png', mimeType: 'image/png', buffer: await transparentEmblem(page) });
    await page.getByRole('dialog').getByRole('button', { name: 'Usar logo' }).click();
    const logo = page.getByRole('button', { name: `Logo de ${name}, opciones` });
    await expect(logo).toBeFocused();

    const before = await logo.locator('img').getAttribute('src');

    // Keyboard only: Enter opens the menu on its first item, "Sustituir".
    await page.keyboard.press('Enter');
    await expect(page.getByRole('menuitem', { name: 'Sustituir' })).toBeFocused();
    const replacement = page.waitForEvent('filechooser');
    await page.keyboard.press('Enter');
    await (
      await replacement
    ).setFiles({ name: 'emblema.png', mimeType: 'image/png', buffer: await transparentEmblem(page) });
    const dialog = page.getByRole('dialog', { name: 'Recortar logo de la comparsa' });
    // The dialog's own keyboard path is covered by PhotoUpload's tests; here the action itself.
    await dialog.getByRole('button', { name: 'Usar logo' }).focus();
    const uploaded = page.waitForResponse(
      (response) =>
        response.request().method() === 'PUT' && response.url().endsWith(`/api/comparsas/${id}/logo`),
    );
    await page.keyboard.press('Enter');
    expect((await uploaded).status()).toBe(200);

    await expect(dialog).toBeHidden();
    await expect(logo).toBeFocused();
    // A new version of the logo, and the change announced.
    await expect(logo.locator('img')).not.toHaveAttribute('src', before ?? '');
    await expect(page.getByRole('status').filter({ hasText: 'Logo guardado' })).toHaveText('Logo guardado');
  });

  test('a tap on the logo opens its menu on a phone', async ({ page, newComparsa, isMobile }) => {
    test.skip(!isMobile, 'Touch is checked on the phone project.');
    const { id, name } = await newComparsa();
    const upload = await page.request.put(`/api/comparsas/${id}/logo`, {
      headers: await antiforgeryHeaders(page),
      multipart: {
        file: { name: 'emblema.png', mimeType: 'image/png', buffer: await transparentEmblem(page) },
      },
    });
    expect(upload.ok()).toBe(true);
    await page.goto(`/comparsas/${id}`);
    await waitForShell(page);

    await page.getByRole('button', { name: `Logo de ${name}, opciones` }).tap();

    await expect(page.getByRole('menuitem', { name: 'Sustituir' })).toBeVisible();
    await expect(page.getByRole('menuitem', { name: 'Quitar' })).toBeVisible();
  });

  test('an Admin uploads a transparent logo, sees it in the header and the list, and removes it', async ({
    page,
    newComparsa,
    axeViolations,
  }) => {
    const { id, name } = await newComparsa();
    await page.goto(`/comparsas/${id}`);
    await waitForShell(page);
    await expect(headerLogo(page).locator('img')).toHaveCount(0);

    const chooser = page.waitForEvent('filechooser');
    await page.getByRole('button', { name: 'Añadir logo de la comparsa' }).click();
    await (
      await chooser
    ).setFiles({ name: 'emblema.png', mimeType: 'image/png', buffer: await transparentEmblem(page) });
    const dialog = page.getByRole('dialog', { name: 'Recortar logo de la comparsa' });
    await expect(dialog.getByRole('img', { name: 'Vista previa del logo recortado' })).toBeVisible();
    expect(await axeViolations(page, '[role="dialog"]')).toEqual([]);
    await dialog.getByRole('button', { name: 'Usar logo' }).click();
    await expect(dialog).toBeHidden();

    const image = headerLogo(page).locator('img');
    await expect(image).toHaveAttribute('src', new RegExp(`^/api/comparsas/${id}/logo\\?v=[0-9a-f-]{36}$`));
    await expect.poll(() => image.evaluate((element: HTMLImageElement) => element.naturalWidth)).toBe(600);
    const url = (await image.getAttribute('src')) ?? '';
    const stored = await page.request.get(url);
    expect(stored.status()).toBe(200);
    expect(stored.headers()['content-type']).toBe('image/png');
    expect(stored.headers()['cache-control']).toContain('no-store');
    expect(stored.headers()['x-content-type-options']).toBe('nosniff');
    expect(stored.headers()['content-disposition']).toBe('inline; filename="logo.png"');
    expect([...(await stored.body()).subarray(0, 4)]).toEqual([0x89, 0x50, 0x4e, 0x47]);
    // The disc is opaque; the background stayed transparent through the crop and the server.
    expect(
      await alphaAt(page, url, [
        [150, 200],
        [550, 50],
      ]),
    ).toEqual([255, 0]);

    await page.goto('/comparsas');
    await waitForShell(page);
    const row = page.getByRole('row').or(page.getByRole('listitem')).filter({ hasText: name });
    await expect(row.locator('[data-slot="comparsa-logo"] img')).toHaveAttribute('src', url);

    await page.goto(`/comparsas/${id}`);
    await waitForShell(page);
    // The logo itself opens its menu (refine-navigation-and-lists D7).
    await page.getByRole('button', { name: /, opciones$/ }).click();
    await page.getByRole('menuitem', { name: 'Quitar' }).click();
    const confirmation = page.getByRole('alertdialog');
    const removed = page.waitForResponse(
      (response) =>
        response.request().method() === 'DELETE' && response.url().endsWith(`/api/comparsas/${id}/logo`),
    );
    await confirmation.getByRole('button', { name: 'Quitar logo' }).click();
    expect((await removed).status()).toBe(204);
    // While the confirmation is open the page is hidden from assistive technology (and from roles).
    await expect(confirmation).toBeHidden();
    await expect(headerLogo(page)).toBeVisible();
    await expect(headerLogo(page).locator('img')).toHaveCount(0);
    expect((await page.request.get(`/api/comparsas/${id}/logo`)).status()).toBe(404);
  });
});

/** The seeded logo of Este, read with the Admin's session: it exists, so a FiringChief's 404 is about scope. */
async function esteLogoAsAdmin(
  playwright: import('@playwright/test').PlaywrightWorkerArgs['playwright'],
): Promise<number> {
  const admin = await playwright.request.newContext({
    baseURL: test.info().project.use.baseURL,
    storageState: ADMIN_STATE,
  });
  try {
    return (await admin.get(`/api/comparsas/${ESTE}/logo`)).status();
  } finally {
    await admin.dispose();
  }
}

test.describe("a FiringChief's comparsa logos", () => {
  test.use({ storageState: FIRING_CHIEF_UNO_STATE });

  test("sees their comparsas with their logos, cannot change them and cannot read another comparsa's", async ({
    page,
    playwright,
    axeViolations,
  }) => {
    await page.goto('/');
    await waitForShell(page);
    await showSidebar(page);

    // By name: another spec may briefly assign Joan Moltó Sala to a comparsa of its own.
    const cards = page.getByRole('navigation', { name: 'Mis comparsas' });
    const norte = cards.getByRole('link', { name: 'Cruzados' });
    const sur = cards.getByRole('link', { name: 'Abencerrajes' });
    await expect(norte.locator('img')).toHaveAttribute(
      'src',
      new RegExp(`^/api/comparsas/${NORTE}/logo\\?v=`),
    );
    await expect
      .poll(() => norte.locator('img').evaluate((element: HTMLImageElement) => element.naturalWidth))
      .toBeGreaterThan(0);
    await expect(sur).toBeVisible();
    await expect(sur.locator('img')).toHaveCount(0);
    expect(await axeViolations()).toEqual([]);

    await norte.click();
    await expect(page.getByRole('heading', { level: 1, name: 'Cruzados' })).toBeVisible();
    // On a phone, choosing a card closes the drawer.
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(headerLogo(page).locator('img')).toHaveCount(1);
    await expect(page.getByRole('button', { name: /logo de la comparsa|, opciones$/ })).toHaveCount(0);

    // Sur has no logo; Este has one (positive control) but is outside this FiringChief's scope.
    expect((await page.request.get(`/api/comparsas/${SUR}/logo`)).status()).toBe(404);
    expect(await esteLogoAsAdmin(playwright)).toBe(200);
    const outside = await page.request.get(`/api/comparsas/${ESTE}/logo`);
    expect(outside.status()).toBe(404);
    expect(outside.headers()['content-type']).not.toBe('image/png');
    const removal = await page.request.delete(`/api/comparsas/${NORTE}/logo`, {
      headers: await antiforgeryHeaders(page),
    });
    expect(removal.status()).toBe(403);

    // The open comparsa is marked as the current card (the drawer is opened again on a phone).
    await showSidebar(page);
    await expect(norte).toHaveAttribute('aria-current', 'page');
  });
});

test.describe("a FiringChief's comparsa logos in the dark theme", () => {
  test.use({ storageState: FIRING_CHIEF_UNO_STATE, colorScheme: 'dark' });

  test('keeps a dark logo visible on its light tile', async ({ page, axeViolations }) => {
    await page.goto('/');
    await waitForShell(page);
    await expect(page.locator('html')).toHaveClass(/dark/);
    await showSidebar(page);

    const norte = page
      .getByRole('navigation', { name: 'Mis comparsas' })
      .getByRole('link', { name: 'Cruzados' });
    const tile = norte.locator('[data-slot="comparsa-logo"]');
    await expect
      .poll(() => norte.locator('img').evaluate((element: HTMLImageElement) => element.naturalWidth))
      .toBeGreaterThan(0);
    // The tile uses the light `--logo-tile` token behind the near-black seeded crescent, not a dark surface.
    const colours = await tile.evaluate((element) => {
      const probe = document.createElement('span');
      probe.style.backgroundColor = getComputedStyle(document.documentElement).getPropertyValue(
        '--logo-tile',
      );
      document.body.append(probe);
      const token = getComputedStyle(probe).backgroundColor;
      probe.remove();
      return { tile: getComputedStyle(element).backgroundColor, token };
    });
    expect(colours.tile).toBe(colours.token);
    expect(await axeViolations()).toEqual([]);
  });
});

test.describe('comparsa logos for an Admin', () => {
  test('show no comparsa cards in the sidebar', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);
    await showSidebar(page);

    await expect(page.getByRole('navigation', { name: 'Navegación principal' })).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Mis comparsas' })).toHaveCount(0);
  });
});
