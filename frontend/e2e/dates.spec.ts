import { FIRING_CHIEF_STATE } from './identity';
import { expect, test, waitForShell } from './fixtures';

/**
 * Native date fields (design D8 of add-arquebusier-registry): each engine renders `type="date"`
 * differently, so typing per segment, a partly typed date and an empty required one are checked in
 * Chromium and Firefox on the register form. Playwright's WebKit builds do not behave like Safari
 * here (a text box on Windows, a field without editable segments on Linux), so WebKit only checks
 * the empty required date; typing a date in Safari is a manual release check. Nothing is submitted.
 */

const NO_SAFARI_DATE_FIELD = "Playwright's WebKit has no Safari-like date field; checked by hand in Safari";

test.use({ storageState: FIRING_CHIEF_STATE });

test.beforeEach(async ({ page }) => {
  await page.goto('/arquebusiers/new');
  await waitForShell(page);
});

test('typing the segments gives an ISO date', async ({ page, browserName }) => {
  test.skip(browserName === 'webkit', NO_SAFARI_DATE_FIELD);
  const birthDate = page.getByLabel(/Fecha de nacimiento/);

  await birthDate.focus();
  await page.keyboard.type('01051990');

  // The segment order follows the browser's own language (day or month first), not the page's.
  await expect(birthDate).toHaveValue(/^1990-(05-01|01-05)$/);
});

test('a partly typed date is reported as incomplete, not as empty', async ({ page, browserName }) => {
  test.skip(browserName === 'webkit', NO_SAFARI_DATE_FIELD);
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
