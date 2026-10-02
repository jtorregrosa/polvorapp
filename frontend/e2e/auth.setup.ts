import { expect, test as setup } from '@playwright/test';
import { ADMIN_STATE, FIRING_CHIEF_STATE, SEEDED, signIn, type Account } from './identity';

/**
 * Signs the seeded users in once per run and saves their sessions, so specs start signed in
 * (Playwright storage state) without spending authenticator codes or rate-limit budget.
 */
async function saveSession(page: import('@playwright/test').Page, account: Account, path: string) {
  await signIn(page, account);
  // Sign-in applies the user's preferred language; the specs expect Spanish, so make it the preference again.
  if ((await page.locator('html').getAttribute('lang')) !== 'es-ES') {
    const saved = page.waitForResponse('**/api/account/locale');
    await page
      .getByRole('banner')
      .getByRole('button', { name: /^(Menú de|Menu for) / })
      .click();
    await page.getByRole('menuitemradio', { name: 'Español' }).click();
    expect((await saved).ok()).toBe(true);
  }
  // Leave the UI language to each spec's browser locale.
  await page.evaluate(() => {
    window.localStorage.removeItem('polvorapp.language');
  });
  await page.context().storageState({ path });
}

// A refused code (another run used this step) is retried with the next one, which may take a moment.
setup.setTimeout(60_000);

setup('sign in the seeded Admin', async ({ page }) => {
  await saveSession(page, SEEDED.admin, ADMIN_STATE);
});

setup('sign in a seeded FiringChief', async ({ page }) => {
  await saveSession(page, SEEDED.firingChief, FIRING_CHIEF_STATE);
});
