import type { Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from '../identity';
import { expect, test, waitForShell } from '../fixtures';

/**
 * Registry lock (BR-10, UC-11; change add-festival-editions). Locking changes what every FiringChief
 * can do, so this spec runs in the `serial-state` project, after the other projects, and unlocks the
 * registry again even when it fails midway. The seeded FiringChief is "Jefa Sintética Dos" (Comparsa
 * Sintética Norte); "Arcabucero Sintético Tres" is seeded in that comparsa.
 */

const SEEDED_IN_NORTE = '0193a300-0000-7000-8000-000000000003';

async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

test.afterEach(async ({ page }) => {
  const response = await page.request.put('/api/registry/lock', {
    headers: await antiforgeryHeaders(page),
    data: { locked: false },
  });
  expect(response.ok(), 'unlocking the registry after the test').toBe(true);
});

test('the Admin locks the registry, the FiringChief can only read it, the Admin still edits', async ({
  page,
  browser,
}) => {
  await page.goto('/arquebusiers');
  await waitForShell(page);
  await page.getByRole('button', { name: 'Bloquear registro' }).click();
  const confirm = page.getByRole('alertdialog', { name: '¿Bloquear el registro?' });
  await confirm.getByRole('button', { name: 'Bloquear registro' }).click();
  await expect(
    page.getByRole('status').filter({ hasText: 'Registro bloqueado para los jefes de disparo.' }),
  ).toHaveCount(1);
  await expect(page.getByRole('button', { name: 'Desbloquear registro' })).toBeFocused();
  await expect(page.getByText('El registro está bloqueado para los jefes de disparo.')).toBeVisible();

  const context = await browser.newContext({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES' });
  try {
    const chief = await context.newPage();
    await chief.goto('/arquebusiers');
    await waitForShell(chief);
    await expect(chief.getByText('La Federación ha bloqueado el registro.')).toBeVisible();
    await expect(chief.getByRole('link', { name: 'Registrar arcabucero' })).toHaveCount(0);

    await chief.goto(`/arquebusiers/${SEEDED_IN_NORTE}`);
    await expect(chief.getByRole('heading', { level: 1, name: 'Arcabucero Sintético Tres' })).toBeVisible();
    await expect(chief.getByText('La Federación ha bloqueado el registro.')).toBeVisible();
    await expect(chief.getByRole('button', { name: /^Editar/ })).toHaveCount(0);
    await expect(chief.getByRole('button', { name: 'Más acciones' })).toHaveCount(0);
  } finally {
    await context.close();
  }

  // The Admin edits while it is locked, then puts the phone back as it was.
  await page.goto(`/arquebusiers/${SEEDED_IN_NORTE}`);
  await page.getByRole('button', { name: 'Editar datos personales' }).click();
  let personal = page.getByRole('dialog', { name: 'Editar datos personales' });
  const phone = personal.getByLabel(/Teléfono/);
  const previous = await phone.inputValue();
  await phone.fill('+34 600 000 098');
  await personal.getByRole('button', { name: 'Guardar cambios' }).click();
  await expect(personal).toBeHidden();
  await expect(page.getByRole('region', { name: 'Datos personales' })).toContainText('+34 600 000 098');

  await page.getByRole('button', { name: 'Editar datos personales' }).click();
  personal = page.getByRole('dialog', { name: 'Editar datos personales' });
  await personal.getByLabel(/Teléfono/).fill(previous);
  await personal.getByRole('button', { name: 'Guardar cambios' }).click();
  await expect(personal).toBeHidden();

  await page.goto('/arquebusiers');
  await page.getByRole('button', { name: 'Desbloquear registro' }).click();
  await page
    .getByRole('alertdialog', { name: '¿Desbloquear el registro?' })
    .getByRole('button', { name: 'Desbloquear registro' })
    .click();
  await expect(page.getByRole('status').filter({ hasText: 'Registro desbloqueado.' })).toHaveCount(1);
  await expect(page.getByRole('button', { name: 'Bloquear registro' })).toBeVisible();
});
