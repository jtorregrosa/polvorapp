import type { Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from '../identity';
import { expect, openNavigation, test, waitForShell } from '../fixtures';

/**
 * The Federation settings (change add-federation-settings) against the seeded stack. Changing the
 * sender name and the lead times affects every email and reminder of the other specs, so this file runs
 * in the `serial-state` project and puts the starting values back after each test.
 */

interface Settings {
  version: number;
  emails: { senderName: string; replyTo: string | null };
  calendar: { milestoneLeadDays: number };
}

/** Headers for a state-changing API call made directly by a test, as the UI sends them. */
async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

async function current(page: Page): Promise<Settings> {
  return (await (await page.request.get('/api/federation-settings')).json()) as Settings;
}

/** The starting values of the settings (the migration's), whatever a failed run left behind. */
async function restoreSettings(page: Page): Promise<void> {
  const headers = await antiforgeryHeaders(page);
  const emails = await page.request.put('/api/federation-settings/emails', {
    headers,
    data: { version: (await current(page)).version, senderName: 'PolvorApp', replyTo: null },
  });
  expect(emails.ok(), 'restoring the email settings').toBe(true);
  const calendar = await page.request.put('/api/federation-settings/calendar', {
    headers,
    data: { version: (await current(page)).version, milestoneLeadDays: 7 },
  });
  expect(calendar.ok(), 'restoring the calendar settings').toBe(true);
}

test.describe('Federation settings as the Admin', () => {
  test.afterEach(async ({ page }) => {
    await restoreSettings(page);
  });

  test('changes the sender name and the milestone lead time, and sees them after a reload', async ({
    page,
  }) => {
    await page.goto('/settings');
    await waitForShell(page);
    await expect(page.getByRole('heading', { level: 1, name: 'Ajustes' })).toBeVisible();

    await page.getByRole('button', { name: 'Editar los correos' }).click();
    let panel = page.getByRole('dialog');
    const senderName = panel.getByRole('textbox', { name: 'Nombre del remitente' });
    await senderName.fill('Unión Sintética · PolvorApp');
    await panel.getByRole('button', { name: 'Guardar cambios' }).click();
    await expect(panel).toBeHidden();

    await page.getByRole('button', { name: 'Editar el calendario' }).click();
    panel = page.getByRole('dialog');
    await panel.getByRole('combobox', { name: 'Recordatorio de hitos' }).selectOption('3');
    await panel.getByRole('button', { name: 'Guardar cambios' }).click();
    await expect(panel).toBeHidden();

    await page.reload();
    await waitForShell(page);
    await expect(page.getByRole('region', { name: 'Correos' })).toContainText(
      'Unión Sintética · PolvorApp (',
    );
    await expect(page.getByRole('region', { name: 'Calendario' })).toContainText('3 días antes');
  });

  test('keeps a refused sender name in the panel with the reason', async ({ page }) => {
    await page.goto('/settings');
    await waitForShell(page);

    await page.getByRole('button', { name: 'Editar los correos' }).click();
    const panel = page.getByRole('dialog');
    await panel.getByRole('textbox', { name: 'Nombre del remitente' }).fill('soporte@banco.example');
    await panel.getByRole('button', { name: 'Guardar cambios' }).click();

    await expect(panel.getByRole('group', { name: 'Hay un problema' })).toBeFocused();
    await expect(panel).toContainText(
      'El nombre no puede llevar comillas, signos de menor o mayor que (< >) ni arroba (@).',
    );
    expect((await current(page)).emails.senderName).toBe('PolvorApp');
  });
});

for (const colorScheme of ['light', 'dark'] as const) {
  test.describe(`the Settings page in the ${colorScheme} theme`, () => {
    test.use({ colorScheme });

    test('has no accessibility violations, nor in a panel', async ({ page, axeViolations }) => {
      await page.goto('/settings');
      await waitForShell(page);
      await expect(page.getByRole('region', { name: 'Calendario' })).toBeVisible();
      await expect(page.getByText('Cargando el logo…')).toHaveCount(0);
      expect(await axeViolations()).toEqual([]);

      await page.getByRole('button', { name: 'Editar la identidad' }).click();
      await expect(page.getByRole('dialog')).toBeVisible();
      await page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running'));
      expect(await axeViolations(page, '[role="dialog"]')).toEqual([]);
    });
  });
}

test.describe('Federation settings as a FiringChief', () => {
  test.use({ storageState: FIRING_CHIEF_STATE });

  test('gets the not-allowed page, no navigation entry and a 403 from the API', async ({ page }) => {
    await page.goto('/settings');
    await expect(page.getByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeVisible();

    const navigation = await openNavigation(page);
    await expect(navigation.getByRole('link', { name: 'Ajustes' })).toHaveCount(0);
    expect((await page.request.get('/api/federation-settings')).status()).toBe(403);
    const change = await page.request.put('/api/federation-settings/emails', {
      headers: await antiforgeryHeaders(page),
      data: { version: 1, senderName: 'Intrusa' },
    });
    expect(change.status()).toBe(403);
  });
});
