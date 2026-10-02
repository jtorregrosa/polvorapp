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
/** "Arcabucera Sintética Seis", seeded in Comparsa Sintética Sur: outside the FiringChief's scope. */
const SEEDED_IN_SUR = '0193a300-0000-7000-8000-000000000006';
const LETTERS = 'TRWAGMYFPDXBNJZSQVHLCKE';

let identities = 0;

/**
 * A valid synthetic identity, unique across workers, calls and runs of the last day: a low DNI
 * number (the seed uses lower ones) and a federation id from 201000, above the seed's 1000xx.
 */
function syntheticIdentity(): { nationalId: string; federationId: string; lastName: string } {
  const second = Math.floor(Date.now() / 1000) % 100_000;
  identities += 1;
  const number = 1000 + ((second * 90 + test.info().parallelIndex * 9 + (identities % 9)) % 9_000_000);
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

/** The arquebusiers list: a table on wide screens, stacked items on a phone. */
const arquebusierList = (page: Page) =>
  page.getByRole('table', { name: 'Arcabuceros' }).or(page.getByRole('list', { name: 'Arcabuceros' }));

const comparsaSelect = (page: Page) => page.getByRole('combobox', { name: 'Comparsa' });

/** Opens a section's edit panel on the detail page (spec: Detail pages in read mode). */
async function editSection(page: Page, name: string) {
  await page.getByRole('button', { name }).click();
  const panel = page.getByRole('dialog', { name });
  await expect(panel).toBeVisible();
  return panel;
}

/** Opens "More actions" of the record header and chooses an item. */
async function moreAction(page: Page, name: string) {
  await page.getByRole('button', { name: 'Más acciones' }).click();
  await page.getByRole('menuitem', { name }).click();
}

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
    await comparsaSelect(page).selectOption({ label: comparsa });
  }
  await page.getByLabel(/ID Unión/).fill(identity.federationId);
  await page.getByLabel(/^DNI\/NIE/).fill(identity.nationalId);
  await page.getByLabel(/^Nombre/).fill('Arcabucera');
  await page.getByLabel(/^Apellidos/).fill(identity.lastName);
  await page.getByLabel(/Fecha de nacimiento/).fill('1990-05-01');
  await page.getByRole('radio', { name: 'Mujer' }).click();
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
    await expect(comparsaSelect(page)).toHaveValue('0193a100-0000-7000-8000-000000000001');
    await page.getByLabel(/ID Unión/).fill(identity.federationId);
    await page.getByLabel(/^DNI\/NIE/).fill(identity.nationalId.toLowerCase());
    await page.getByLabel(/^Nombre/).fill('Arcabucera');
    await page.getByLabel(/^Apellidos/).fill(identity.lastName);
    await page.getByLabel(/Fecha de nacimiento/).fill('1990-05-01');
    await page.getByRole('radio', { name: 'Mujer' }).click();
    await page.getByRole('radio', { name: /^AE/ }).click();
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
    await expect(page.getByRole('region', { name: 'Datos personales' })).toContainText(identity.nationalId);

    await page.getByRole('link', { name: 'Añadir arma propia' }).click();
    await page.getByLabel(/^Modelo/).selectOption({ label: 'PISTOLA' });
    await page.getByLabel(/^Nº de arma/).fill('E2E-1');
    await page.getByLabel(/^Nº de guía/).fill(`e2e-${identity.federationId}`);
    await page.getByRole('button', { name: 'Añadir arma' }).click();
    await expect(page).toHaveURL(`/arquebusiers/${id}`);
    await expect(
      page
        .getByRole('table', { name: /^Armas propias de / })
        .or(page.getByRole('list', { name: /^Armas propias de / }))
        .getByText(`E2E-${identity.federationId}`),
    ).toBeVisible();

    const personal = await editSection(page, 'Editar datos personales');
    await personal.getByLabel(/Teléfono/).fill('+34 600 000 099');
    await personal.getByRole('button', { name: 'Guardar cambios' }).click();
    await expect(personal).toBeHidden();
    await expect(notice(page, 'Cambios guardados')).toHaveCount(1);
    await expect(page.getByRole('button', { name: 'Editar datos personales' })).toBeFocused();
    await moreAction(page, 'Pasar a reserva');
    await expect(notice(page, 'está ahora en Reserva')).toHaveCount(1);
    await page.reload();
    await expect(page.getByRole('region', { name: 'Datos personales' })).toContainText('+34 600 000 099');
    await expect(page.locator('main header').getByText('Reserva')).toBeVisible();
    await expect(page.getByRole('region', { name: 'Licencia' })).toContainText('10/03/2029');
    await expect(page.getByRole('region', { name: 'Curso de arcabucería' })).toContainText('15/11/2025');

    await moreAction(page, 'Eliminar arcabucero');
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

    const list = arquebusierList(page);
    await expect(list.getByText(NORTE).first()).toBeVisible();
    await expect(list.getByText(SUR)).toHaveCount(0);
  });

  test('refuses a DNI/NIE already registered, and takes it again once that arquebusier is deleted', async ({
    page,
    deleteAfter,
  }) => {
    test.slow();
    const first = await register(page);
    deleteAfter(first.id);

    const again = syntheticIdentity();
    await page.goto('/arquebusiers/new');
    await waitForShell(page);
    await page.getByLabel(/ID Unión/).fill(again.federationId);
    await page.getByLabel(/^DNI\/NIE/).fill(first.nationalId);
    await page.getByLabel(/^Nombre/).fill('Arcabucera');
    await page.getByLabel(/^Apellidos/).fill(again.lastName);
    await page.getByLabel(/Fecha de nacimiento/).fill('1990-05-01');
    await page.getByRole('radio', { name: 'Mujer' }).click();
    await page.getByRole('button', { name: 'Registrar arcabucero' }).click();
    // BR-01: blocking, on the field, without saying whose it is.
    await expect(page.getByLabel(/^DNI\/NIE/)).toHaveAccessibleDescription(/contacta con la Federación/);
    await expect(page).toHaveURL('/arquebusiers/new');

    const headers = await antiforgeryHeaders(page);
    expect((await page.request.delete(`/api/arquebusiers/${first.id}`, { headers })).status()).toBe(204);
    await page.getByRole('button', { name: 'Registrar arcabucero' }).click();

    await expect(page).toHaveURL(/\/arquebusiers\/[0-9a-f-]{36}$/);
    deleteAfter(idFromUrl(page));
    await expect(page.getByRole('region', { name: 'Datos personales' })).toContainText(first.nationalId);
  });

  // The date fields are checked per engine in dates.spec.ts.
  test('explains a wrong DNI letter in the error summary, which takes focus, and at the field', async ({
    page,
  }) => {
    await page.goto('/arquebusiers/new');
    await waitForShell(page);

    await page.getByLabel(/^DNI\/NIE/).fill('12345678A');
    await page.getByRole('button', { name: 'Registrar arcabucero' }).click();

    const summary = page.getByRole('group', { name: 'Hay un problema' });
    await expect(summary).toBeFocused();
    await summary.getByRole('link', { name: /^DNI\/NIE: La letra no corresponde/ }).click();
    await expect(page.getByLabel(/^DNI\/NIE/)).toBeFocused();
    await expect(page.getByLabel(/^DNI\/NIE/)).toHaveAccessibleDescription(/La letra no corresponde/);
  });

  test('never hides the focused field under the action bar (SC 2.4.11)', async ({ page }) => {
    await page.goto('/arquebusiers/new');
    await waitForShell(page);
    const bar = page.locator('[data-slot="action-bar"]');

    for (let step = 0; step < 24; step += 1) {
      await page.keyboard.press('Tab');
      const covered = await page.evaluate(() => {
        const focused = document.activeElement;
        const actions = document.querySelector('[data-slot="action-bar"]');
        if (!focused || !actions || actions.contains(focused) || focused === document.body) return false;
        return focused.getBoundingClientRect().bottom > actions.getBoundingClientRect().top + 1;
      });
      expect(covered, `step ${String(step)}`).toBe(false);
    }
    await expect(bar).toBeVisible();
  });

  test('filters the list with a counter and announces the count', async ({ page }) => {
    await page.goto('/arquebusiers');
    await waitForShell(page);
    const counters = page.getByRole('group', { name: 'Resumen de arcabuceros' });
    const active = counters.getByRole('button', { name: /En activo/ });

    await active.click();

    await expect(active).toHaveAttribute('aria-pressed', 'true');
    await expect(page).toHaveURL(/status=ACTIVE/);
    await expect(page.getByRole('status').filter({ hasText: /arcabucer/ })).toHaveCount(1);
    await active.click();
    await expect(active).toHaveAttribute('aria-pressed', 'false');
  });
});

test.describe('arquebusier registry as an Admin', () => {
  test('filters by license and comparsa, keeps the filters in the address and clears them', async ({
    page,
  }) => {
    await page.goto('/arquebusiers');
    await waitForShell(page);
    const counters = page.getByRole('group', { name: 'Resumen de arcabuceros' });
    const expired = counters.getByRole('button', { name: /Licencia caducada/ });
    const list = arquebusierList(page);

    await expired.click();
    await expect(expired).toHaveAttribute('aria-pressed', 'true');
    await expect(page).toHaveURL(/license=EXPIRED/);
    await expect(list.getByText('Caducada').first()).toBeVisible();
    await expect(list.getByText('Vigente')).toHaveCount(0);
    await page.reload();
    await waitForShell(page);
    await expect(expired).toHaveAttribute('aria-pressed', 'true');

    await comparsaSelect(page).selectOption({ label: SUR });
    await expect(page).toHaveURL(/comparsaId=/);
    await expect(list.getByText(NORTE)).toHaveCount(0);

    await page.getByRole('searchbox').fill('zzzz sin coincidencias');
    await expect(
      page.getByRole('heading', { name: 'Ningún arcabucero coincide con la búsqueda o los filtros.' }),
    ).toBeVisible();
    await page.getByRole('button', { name: 'Quitar los filtros' }).click();
    await expect(page).toHaveURL(/\/arquebusiers$/);
    await expect(expired).toHaveAttribute('aria-pressed', 'false');
    await expect(list.getByText(NORTE).first()).toBeVisible();
    await expect(page.locator('[data-slot="filter-bar"]').locator('input, select').first()).toBeFocused();
  });

  test('lists every missing field in the error summary and links each one to its field', async ({ page }) => {
    await page.goto('/arquebusiers/new');
    await waitForShell(page);

    await page.getByRole('button', { name: 'Registrar arcabucero' }).click();

    const summary = page.getByRole('group', { name: 'Hay un problema' });
    await expect(summary).toBeFocused();
    for (const name of [
      /^Comparsa/,
      /^ID Unión/,
      /^DNI\/NIE/,
      /^Nombre/,
      /^Apellidos/,
      /^Fecha de nacimiento/,
      /^Género/,
    ]) {
      await expect(summary.getByRole('link', { name })).toBeVisible();
    }
    await summary.getByRole('link', { name: /^Apellidos/ }).click();
    const lastName = page.getByLabel(/^Apellidos/);
    await expect(lastName).toBeFocused();
    await lastName.fill('Sintética');
    // A fixed field leaves the summary at once.
    await expect(summary.getByRole('link', { name: /^Apellidos/ })).toHaveCount(0);
    await expect(summary.getByRole('link', { name: /^Nombre/ })).toBeVisible();
  });

  test("transfers an arquebusier, after which the previous comparsa's FiringChief no longer sees it", async ({
    page,
    browser,
    deleteAfter,
  }) => {
    test.slow();
    const arquebusier = await register(page, NORTE);
    deleteAfter(arquebusier.id);
    const chief = await firingChiefPage(browser);
    const chiefRow = arquebusierList(chief).getByText(arquebusier.nationalId);
    try {
      await chief.goto('/arquebusiers');
      await expect(chiefRow).toBeVisible();
      await chief.goto(`/arquebusiers/${arquebusier.id}`);
      await expect(chief.getByRole('heading', { level: 1, name: arquebusier.name })).toBeVisible();

      await page.getByRole('button', { name: 'Trasladar' }).click();
      await page.getByRole('combobox', { name: 'Comparsa de destino' }).selectOption({ label: SUR });
      const dialog = page.getByRole('alertdialog', { name: `¿Trasladar a ${arquebusier.name} a ${SUR}?` });
      await dialog.getByRole('button', { name: 'Trasladar' }).click();

      await expect(notice(page, `pertenece ahora a ${SUR}`)).toBeFocused();
      await chief.reload();
      await expect(chief.getByRole('heading', { level: 1, name: 'Página no encontrada' })).toBeVisible();
      await chief.goto('/arquebusiers');
      await expect(arquebusierList(chief).getByRole('link').first()).toBeVisible();
      await expect(chiefRow).toHaveCount(0);
    } finally {
      await chief.context().close();
    }
  });

  test('cannot delete a comparsa that has arquebusiers', async ({ page }) => {
    // A comparsa of its own: if the guard ever broke, no seeded comparsa would be lost.
    const headers = await antiforgeryHeaders(page);
    const identity = syntheticIdentity();
    const created = await page.request.post('/api/comparsas', {
      headers,
      data: { name: `Comparsa E2E ${identity.federationId}`, side: 'MOORISH' },
    });
    expect(created.status()).toBe(201);
    const comparsaId = ((await created.json()) as { id: string }).id;
    let arquebusierId: string | undefined;
    try {
      const registered = await page.request.post('/api/arquebusiers', {
        headers,
        data: {
          comparsaId,
          federationId: Number(identity.federationId),
          nationalId: identity.nationalId,
          firstName: 'Arcabucera',
          lastName: identity.lastName,
          birthDate: '1990-05-01',
          email: null,
          phone: null,
          gender: 'FEMALE',
          status: 'ACTIVE',
          trainingCompletedOn: null,
          license: null,
        },
      });
      expect(registered.status()).toBe(201);
      arquebusierId = ((await registered.json()) as { id: string }).id;

      await page.goto(`/comparsas/${comparsaId}`);
      await waitForShell(page);
      await moreAction(page, 'Eliminar comparsa');
      const dialog = page.getByRole('alertdialog');
      await dialog.getByRole('button', { name: 'Eliminar' }).click();

      await expect(dialog).toContainText('Otros registros usan esta comparsa');
      await dialog.getByRole('button', { name: 'Cancelar' }).click();
      expect((await page.request.get(`/api/comparsas/${comparsaId}`)).status()).toBe(200);
    } finally {
      // The arquebusier first, so the comparsa is no longer in use.
      const cleanup = [
        ...(arquebusierId ? [`/api/arquebusiers/${arquebusierId}`] : []),
        `/api/comparsas/${comparsaId}`,
      ];
      for (const resource of cleanup) {
        expect([204, 404], `cleanup of ${resource}`).toContain(
          (await page.request.delete(resource, { headers })).status(),
        );
      }
    }
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
