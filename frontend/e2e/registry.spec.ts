import type { Browser, Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from './identity';
import { expect, test as base, waitForShell } from './fixtures';

/**
 * Arquebusier registry (change add-arquebusier-registry) against the seeded stack: the synthetic
 * comparsas, catalogue and arquebusiers (docs/development.md). The signed-in FiringChief is "Jefa
 * Sintética Dos" (Comparsa Sintética Norte). Each test registers the arquebusiers it changes, with
 * unique synthetic identifiers, and deletes them again, so the desktop and mobile projects can run
 * side by side.
 */

const NORTE = 'Comparsa Sintética Norte';
const SUR = 'Comparsa Sintética Sur';
const SEEDED_ESTE = '0193a100-0000-7000-8000-000000000003';
/** "Arcabucera Sintética Seis", seeded in Comparsa Sintética Sur: outside the FiringChief's scope. */
const SEEDED_IN_SUR = '0193a300-0000-7000-8000-000000000006';
const LETTERS = 'TRWAGMYFPDXBNJZSQVHLCKE';

/** A unique, valid synthetic identity: a very low DNI number and a federation id of the 2xxxxx range. */
function syntheticIdentity(): { nationalId: string; federationId: string; lastName: string } {
  const number = 1000 + Math.floor(Math.random() * 98_000);
  return {
    nationalId: `${String(number).padStart(8, '0')}${LETTERS[number % LETTERS.length]}`,
    federationId: String(200_000 + number),
    lastName: `Sintética E2E ${number}`,
  };
}

async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

const test = base.extend<{
  /** Deletes the arquebusiers a test registered, even when the test failed midway. */
  deleteAfter: (id: string) => void;
}>({
  deleteAfter: async ({ page }, use) => {
    const ids: string[] = [];
    await use((id) => ids.push(id));
    if (ids.length === 0) return;
    const headers = await antiforgeryHeaders(page);
    // Every one is attempted before failing, so one broken cleanup does not leak the others.
    const failed: string[] = [];
    for (const id of ids) {
      const response = await page.request.delete(`/api/arquebusiers/${id}`, { headers });
      if (![204, 404].includes(response.status())) failed.push(`${id}: ${response.status()}`);
    }
    expect(failed, 'cleanup of registered arquebusiers').toEqual([]);
  },
});

const idFromUrl = (page: Page): string => new URL(page.url()).pathname.split('/').pop() ?? '';

const notice = (page: Page, text: string | RegExp) => page.getByRole('status').filter({ hasText: text });

/**
 * WCAG 2.5.8 minimum target size: controls in the main content measure at least 24 by 24 CSS pixels,
 * or are spaced so that a 24 px circle centred on them meets no other target (the spacing exception,
 * e.g. a name link alone in a table row).
 */
async function expectTargetsOfAtLeast24px(page: Page): Promise<void> {
  const failing = await page.evaluate(() => {
    const MIN = 24;
    const targets = [
      ...document.querySelectorAll(
        'main :is(button, a[href], input, select):not([aria-hidden="true"], [tabindex="-1"])',
      ),
    ]
      .map((element) => ({ element, box: element.getBoundingClientRect() }))
      .filter(({ box }) => box.width > 0 && box.height > 0);
    const small = (box: DOMRect) => box.width < MIN || box.height < MIN;
    const centre = (box: DOMRect) => ({ x: box.left + box.width / 2, y: box.top + box.height / 2 });
    const distanceToBox = (point: { x: number; y: number }, box: DOMRect) =>
      Math.hypot(
        Math.max(box.left - point.x, 0, point.x - box.right),
        Math.max(box.top - point.y, 0, point.y - box.bottom),
      );
    return targets
      .filter(({ box }) => small(box))
      .filter(({ element, box }) =>
        targets.some((other) => {
          if (other.element === element || other.element.contains(element) || element.contains(other.element))
            return false;
          const here = centre(box);
          return small(other.box)
            ? Math.hypot(here.x - centre(other.box).x, here.y - centre(other.box).y) < MIN
            : distanceToBox(here, other.box) < MIN / 2;
        }),
      )
      .map(({ element, box }) => `${element.outerHTML.slice(0, 80)} ${box.width}x${box.height}`);
  });
  expect(failing).toEqual([]);
}

async function expectNoHorizontalOverflow(page: Page): Promise<void> {
  const overflows = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  expect(overflows).toBe(false);
}

/** Registers an arquebusier through the form, as the signed-in user, and returns its id. */
async function register(
  page: Page,
  comparsa?: string,
): Promise<{ id: string; name: string; nationalId: string }> {
  const identity = syntheticIdentity();
  await page.goto('/arquebusiers/new');
  await waitForShell(page);
  if (comparsa) {
    await page.getByLabel(/^Comparsa/).selectOption({ label: comparsa });
  }
  await page.getByLabel(/ID Unión/).fill(identity.federationId);
  await page.getByLabel(/^DNI\/NIE/).fill(identity.nationalId);
  await page.getByLabel(/^Nombre/).fill('Arcabucera');
  await page.getByLabel(/^Apellidos/).fill(identity.lastName);
  await page.getByLabel(/Fecha de nacimiento/).fill('1990-05-01');
  await page.getByLabel(/Género/).selectOption('FEMALE');
  await page.getByRole('button', { name: 'Registrar arcabucero' }).click();
  await expect(page).toHaveURL(/\/arquebusiers\/[0-9a-f-]{36}$/);
  return { id: idFromUrl(page), name: `Arcabucera ${identity.lastName}`, nationalId: identity.nationalId };
}

async function firingChiefPage(browser: Browser): Promise<Page> {
  const context = await browser.newContext({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES' });
  return context.newPage();
}

test.describe('arquebusier registry as a FiringChief', () => {
  test.use({ storageState: FIRING_CHIEF_STATE });

  test('registers an arquebusier with license and course, adds an owned weapon, edits, sets Reserve and deletes', async ({
    page,
    deleteAfter,
  }) => {
    test.slow();
    const identity = syntheticIdentity();
    await page.goto('/arquebusiers/new');
    await waitForShell(page);
    // The FiringChief's only active comparsa is pre-selected.
    await expect(page.getByLabel(/^Comparsa/)).toHaveValue('0193a100-0000-7000-8000-000000000001');
    await page.getByLabel(/ID Unión/).fill(identity.federationId);
    await page.getByLabel(/^DNI\/NIE/).fill(identity.nationalId.toLowerCase());
    await page.getByLabel(/^Nombre/).fill('Arcabucera');
    await page.getByLabel(/^Apellidos/).fill(identity.lastName);
    await page.getByLabel(/Fecha de nacimiento/).fill('1990-05-01');
    await page.getByLabel(/Género/).selectOption('FEMALE');
    await page.getByLabel(/Tipo de licencia/).selectOption('AE');
    await page.getByLabel(/Fecha de expedición/).fill('2024-03-10');
    await expect(page.getByLabel(/Fecha de caducidad/)).toHaveValue('2029-03-10');
    await page.getByLabel(/Fecha del curso/).fill('2025-11-15');
    await page.getByRole('button', { name: 'Registrar arcabucero' }).click();

    await expect(page).toHaveURL(/\/arquebusiers\/[0-9a-f-]{36}$/);
    const id = idFromUrl(page);
    deleteAfter(id);
    await expect(notice(page, 'registrado')).toBeFocused();
    await expect(
      page.getByRole('heading', { level: 1, name: `Arcabucera ${identity.lastName}` }),
    ).toBeVisible();
    await expect(page.getByLabel(/^DNI\/NIE/)).toHaveValue(identity.nationalId);

    await page.getByRole('link', { name: 'Añadir arma propia' }).click();
    await page.getByLabel(/^Modelo/).selectOption({ label: 'PISTOLA' });
    await page.getByLabel(/^Nº de arma/).fill('E2E-1');
    await page.getByLabel(/^Nº de guía/).fill(`e2e-${identity.federationId}`);
    await page.getByRole('button', { name: 'Añadir arma' }).click();
    await expect(page).toHaveURL(`/arquebusiers/${id}`);
    await expect(
      page.getByRole('table', { name: /^Armas propias de / }).getByText(`E2E-${identity.federationId}`),
    ).toBeVisible();

    await page.getByLabel(/Teléfono/).fill('+34 600 000 099');
    await page.getByLabel(/^Estado/).selectOption('RESERVE');
    await page.getByRole('button', { name: 'Guardar cambios' }).click();
    await expect(notice(page, 'Cambios guardados.')).toBeVisible();
    await page.reload();
    await expect(page.getByLabel(/Teléfono/)).toHaveValue('+34 600 000 099');
    await expect(page.getByLabel(/^Estado/)).toHaveValue('RESERVE');

    await page.getByRole('button', { name: 'Eliminar arcabucero' }).click();
    const dialog = page.getByRole('alertdialog', { name: `¿Eliminar a Arcabucera ${identity.lastName}?` });
    await expect(dialog).toContainText('No se puede deshacer');
    await expect(dialog).toContainText('Reserva');
    await dialog.getByRole('button', { name: 'Eliminar' }).click();
    await expect(page).toHaveURL('/arquebusiers');
    await expect(notice(page, 'se ha eliminado del registro')).toBeFocused();
    await page.goto(`/arquebusiers/${id}`);
    await expect(page.getByRole('heading', { level: 1, name: 'Página no encontrada' })).toBeVisible();
  });

  test('cannot open an arquebusier of another comparsa by its address', async ({ page }) => {
    await page.goto(`/arquebusiers/${SEEDED_IN_SUR}`);

    await expect(page.getByRole('heading', { level: 1, name: 'Página no encontrada' })).toBeVisible();
  });

  test('sees only the arquebusiers of their comparsa', async ({ page }) => {
    await page.goto('/arquebusiers');
    await waitForShell(page);

    const table = page.getByRole('table', { name: 'Arcabuceros' });
    await expect(table.getByRole('link').first()).toBeVisible();
    await expect(table.getByText(SUR)).toHaveCount(0);
  });

  // The date fields are checked per engine in dates.spec.ts.
  test('explains a wrong DNI letter as soon as the field is left', async ({ page }) => {
    await page.goto('/arquebusiers/new');
    await waitForShell(page);

    await page.getByLabel(/^DNI\/NIE/).fill('12345678A');
    await page.getByLabel(/^Nombre/).focus();

    await expect(page.getByText('La letra no corresponde a los números.')).toBeVisible();
  });
});

test.describe('arquebusier registry as an Admin', () => {
  test("transfers an arquebusier, after which the previous comparsa's FiringChief no longer sees it", async ({
    page,
    browser,
    deleteAfter,
  }) => {
    test.slow();
    const arquebusier = await register(page, NORTE);
    deleteAfter(arquebusier.id);
    const chief = await firingChiefPage(browser);
    try {
      await chief.goto(`/arquebusiers/${arquebusier.id}`);
      await expect(chief.getByRole('heading', { level: 1, name: arquebusier.name })).toBeVisible();

      await page.getByRole('combobox', { name: 'Comparsa de destino' }).selectOption({ label: SUR });
      await page.getByRole('button', { name: 'Trasladar' }).click();
      const dialog = page.getByRole('alertdialog', { name: `¿Trasladar a ${arquebusier.name} a ${SUR}?` });
      await dialog.getByRole('button', { name: 'Trasladar' }).click();

      await expect(notice(page, `pertenece ahora a ${SUR}`)).toBeFocused();
      await chief.reload();
      await expect(chief.getByRole('heading', { level: 1, name: 'Página no encontrada' })).toBeVisible();
    } finally {
      await chief.context().close();
    }
  });

  test('cannot delete a comparsa that has arquebusiers', async ({ page }) => {
    await page.goto(`/comparsas/${SEEDED_ESTE}`);
    await waitForShell(page);

    await page.getByRole('button', { name: 'Eliminar comparsa' }).click();
    const dialog = page.getByRole('alertdialog');
    await dialog.getByRole('button', { name: 'Eliminar' }).click();

    await expect(dialog).toContainText('Otros registros usan esta comparsa');
    await dialog.getByRole('button', { name: 'Cancelar' }).click();
  });
});

test.describe('registry screens on a phone', () => {
  test.use({ viewport: { width: 375, height: 740 } });

  for (const theme of ['light', 'dark'] as const) {
    test(`fit 375 px without horizontal scrolling and pass axe in the ${theme} theme`, async ({
      page,
      axeViolations,
      deleteAfter,
    }) => {
      await page.emulateMedia({ colorScheme: theme });
      const arquebusier = await register(page, NORTE);
      deleteAfter(arquebusier.id);

      for (const path of [
        '/arquebusiers',
        '/arquebusiers/new',
        `/arquebusiers/${arquebusier.id}`,
        `/arquebusiers/${arquebusier.id}/weapons/new`,
      ]) {
        await page.goto(path);
        await waitForShell(page);
        await expectNoHorizontalOverflow(page);
        await expectTargetsOfAtLeast24px(page);
        expect(await axeViolations(), path).toEqual([]);
      }
    });
  }
});
