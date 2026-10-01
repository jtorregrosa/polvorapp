import { FIRING_CHIEF_STATE } from './identity';
import { expect, test, waitForShell } from './fixtures';

/**
 * Native date fields (design D8 of add-arquebusier-registry): each engine renders `type="date"`
 * differently, so typing per segment, a partly typed date and an empty required one are checked in
 * Chromium, Firefox and WebKit on the register form. Nothing is submitted to the server.
 */

test.use({ storageState: FIRING_CHIEF_STATE });

test.beforeEach(async ({ page }) => {
  await page.goto('/arquebusiers/new');
  await waitForShell(page);
});

test('typing the segments gives an ISO date', async ({ page }) => {
  const birthDate = page.getByLabel(/Fecha de nacimiento/);
  // Some engines (Playwright's WebKit on Windows) have no date field and show a text box instead:
  // there the form rejects free text as an incomplete date, which the other tests cover.
  const native = await birthDate.evaluate((input) => (input as HTMLInputElement).type === 'date');
  test.skip(!native, 'this engine has no native date field');

  await birthDate.focus();
  await page.keyboard.type('01051990');

  // The segment order follows the browser's own language (day or month first), not the page's.
  await expect(birthDate).toHaveValue(/^1990-(05-01|01-05)$/);
});

test('a partly typed date is reported as incomplete, not as empty', async ({ page }) => {
  const birthDate = page.getByLabel(/Fecha de nacimiento/);

  await birthDate.focus();
  await page.keyboard.type('0105');
  await page.getByRole('button', { name: 'Registrar arcabucero' }).click();

  await expect(birthDate).toHaveAccessibleDescription(/fecha completa/);
});

test('an empty required date says it is required', async ({ page }) => {
  await page.getByRole('button', { name: 'Registrar arcabucero' }).click();

  await expect(page.getByLabel(/Fecha de nacimiento/)).toHaveAccessibleDescription(/obligatorio/);
});
