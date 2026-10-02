import { expect, openNavigation, test, waitForShell } from './fixtures';
import { FIRING_CHIEF_STATE, inviteFiringChief, linkFromEmail, signIn, uniqueEmail } from './identity';

const SIGNED_OUT = { cookies: [], origins: [] };
/** A seeded FiringChief (docs/development.md), never changed by these tests. */
const JEFE_UNO = '0193a000-0000-7000-8000-000000000002';

test.describe('identity and access', () => {
  // Whole journeys with new users: once per run is enough, and it keeps within the auth rate limits.
  test.skip(({ isMobile }) => isMobile, 'journeys run on the desktop project');
  // Each journey invites and enrols a user and waits for emails.
  test.setTimeout(90_000);

  test('an invited FiringChief accepts, enrols, signs in and does not see Users', async ({
    browser,
    page,
  }) => {
    const account = await inviteFiringChief(browser, page, 'invitee');
    const context = await browser.newContext({ storageState: SIGNED_OUT });
    const invitee = await context.newPage();

    await signIn(invitee, account);

    const navigation = await openNavigation(invitee);
    await expect(navigation.getByRole('link', { name: 'Inicio' })).toBeVisible();
    await expect(navigation.getByRole('link', { name: 'Usuarios' })).toHaveCount(0);
    await invitee.goto('/users');
    await expect(invitee.getByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeVisible();
    expect((await invitee.request.get('/api/users')).status()).toBe(403);
    await context.close();
  });

  test('a remembered device skips the code for the next sign-in', async ({ browser, page }) => {
    const account = await inviteFiringChief(browser, page, 'remembered');
    const context = await browser.newContext({ storageState: SIGNED_OUT });
    const user = await context.newPage();
    await signIn(user, account, { rememberDevice: true });

    await user.getByRole('button', { name: /^Menú de / }).click();
    await user.getByRole('menuitem', { name: 'Cerrar sesión' }).click();
    await expect(user.getByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeVisible();
    await user.getByLabel(/^Correo electrónico/).fill(account.email);
    await user.getByLabel(/^Contraseña/).fill(account.password);
    await user.getByRole('button', { name: 'Continuar' }).click();

    await expect(user.getByRole('heading', { level: 1, name: 'Inicio' })).toBeVisible();
    await expect(user).toHaveURL('/');
    await context.close();
  });

  test('a forgotten password is reset from the emailed link', async ({ browser, page }) => {
    const account = await inviteFiringChief(browser, page, 'reset');
    const context = await browser.newContext({ storageState: SIGNED_OUT });
    const user = await context.newPage();
    await user.goto('/login');
    await user.getByRole('link', { name: '¿Has olvidado la contraseña?' }).click();
    // The sign-in page has an email field too: fill only once the recovery page is shown.
    await expect(user.getByRole('heading', { level: 1, name: 'Recuperar la contraseña' })).toBeVisible();
    await user.getByLabel(/^Correo electrónico/).fill(account.email);
    await user.getByRole('button', { name: 'Enviar enlace' }).click();
    await expect(user.getByRole('heading', { level: 1, name: 'Revisa tu correo' })).toBeVisible();

    await user.goto(await linkFromEmail(user, account.email, 'password/reset'));
    const password = `${account.password} renewed`;
    await user.getByLabel(/^Contraseña nueva/).fill(password);
    await user.getByLabel(/^Repite la contraseña nueva/).fill(password);
    await user.getByRole('button', { name: 'Guardar contraseña' }).click();
    await expect(
      user.getByRole('heading', { level: 1, name: 'Contraseña cambiada. Ya puedes iniciar sesión.' }),
    ).toBeVisible();

    await signIn(user, { ...account, password });
    await context.close();
  });

  test('a deactivated user is signed out', async ({ browser, page }) => {
    const account = await inviteFiringChief(browser, page, 'deactivated');
    const context = await browser.newContext({ storageState: SIGNED_OUT });
    const user = await context.newPage();
    await signIn(user, account);

    await page.goto(account.detailPath);
    await expect(page.getByRole('heading', { level: 1, name: account.name })).toBeVisible();
    await page.getByRole('button', { name: 'Más acciones' }).click();
    await page.getByRole('menuitem', { name: 'Desactivar usuario' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Desactivar usuario' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Usuario desactivado.' })).toHaveCount(1);
    await expect(page.getByRole('button', { name: 'Más acciones' })).toBeFocused();

    expect((await user.request.get('/api/account')).status()).toBe(401);
    await user.reload();

    await expect(user.getByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeVisible();
    await context.close();
  });

  test('an unknown email gets the same message as a wrong password', async ({ browser }) => {
    const context = await browser.newContext({ storageState: SIGNED_OUT });
    const page = await context.newPage();
    await page.goto('/login');
    await page.getByLabel(/^Correo electrónico/).fill(uniqueEmail('unknown'));
    await page.getByLabel(/^Contraseña/).fill('not the password');
    await page.getByRole('button', { name: 'Continuar' }).click();

    await expect(page.getByText('El correo o la contraseña no son válidos.')).toBeVisible();
    await context.close();
  });
});

test.describe('signing in (SC 3.3.8 accessible authentication)', () => {
  test.use({ storageState: SIGNED_OUT });

  test('accepts pasted email and password, as from a password manager', async ({ page, browserName }) => {
    test.skip(browserName !== 'chromium', 'clipboard permissions are granted in Chromium only');
    await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
    await page.goto('/login');
    await expect(page.getByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeVisible();

    for (const [label, text] of [
      [/^Correo electrónico/, 'pegado@polvorapp.example'],
      [/^Contraseña/, 'a pasted passphrase'],
    ] as const) {
      const field = page.getByLabel(label);
      await page.evaluate((value) => navigator.clipboard.writeText(value), text);
      await field.focus();
      await page.keyboard.press('ControlOrMeta+V');
      await expect(field).toHaveValue(text);
    }
    // The password can be shown to check what was pasted.
    await page.getByRole('button', { name: 'Mostrar la contraseña' }).click();
    await expect(page.getByLabel(/^Contraseña/)).toHaveAttribute('type', 'text');
    await expect(page.getByLabel(/^Correo electrónico/)).toHaveAttribute('autocomplete', 'username');
  });
});

test.describe('as a FiringChief', () => {
  test.use({ storageState: FIRING_CHIEF_STATE });

  test('the Users entry is hidden and the page is not allowed', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);

    const navigation = await openNavigation(page);
    await expect(navigation.getByRole('link', { name: 'Usuarios' })).toHaveCount(0);
    await page.goto('/users');
    await expect(page.getByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeVisible();
    expect((await page.request.get('/api/users')).status()).toBe(403);
  });
});

test.describe('accessibility of the identity pages', () => {
  for (const colorScheme of ['light', 'dark'] as const) {
    test.describe(`${colorScheme} theme`, () => {
      test.use({ colorScheme });

      test('sign-in page', async ({ browser, axeViolations }) => {
        const context = await browser.newContext({ storageState: SIGNED_OUT, colorScheme });
        const page = await context.newPage();
        await page.goto('/login');
        await expect(page.getByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeVisible();

        expect(await axeViolations(page)).toEqual([]);
        await context.close();
      });

      test('users page', async ({ page, axeViolations }) => {
        await page.goto('/users');
        // A table on wide screens, stacked items on a phone.
        const users = page
          .getByRole('table', { name: 'Usuarios' })
          .or(page.getByRole('list', { name: 'Usuarios' }));
        await expect(users.getByRole('link', { name: 'Admin Sintética' })).toBeVisible();

        expect(await axeViolations(page)).toEqual([]);
      });

      test('user page, read-only and with its edit panel open', async ({ page, axeViolations }) => {
        await page.goto(`/users/${JEFE_UNO}`);
        await waitForShell(page);
        await expect(page.getByRole('region', { name: 'Datos de la cuenta' })).toBeVisible();
        expect(await axeViolations(page)).toEqual([]);

        await page.getByRole('button', { name: 'Editar datos de la cuenta' }).click();
        const panel = page.getByRole('dialog', { name: 'Editar datos de la cuenta' });
        await expect(panel.getByRole('radio', { name: 'Jefe de disparo' })).toBeChecked();
        // Let the panel finish sliding in: axe reads colours mid-animation otherwise.
        await page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running'));
        expect(await axeViolations(page, '[role="dialog"]')).toEqual([]);
        await page.keyboard.press('Escape');
        await expect(page.getByRole('button', { name: 'Editar datos de la cuenta' })).toBeFocused();
      });

      test('account page', async ({ page, axeViolations }) => {
        await page.goto('/account');
        await waitForShell(page);
        await expect(page.getByRole('region', { name: 'Cambiar la contraseña' })).toBeVisible();

        expect(await axeViolations(page)).toEqual([]);
      });
    });
  }

  test('enrolment page in both themes', async ({ browser, page, axeViolations, isMobile }) => {
    test.skip(isMobile, 'journeys run on the desktop project');
    const email = uniqueEmail('a11y');
    await page.goto('/users/new');
    await page.getByLabel(/^Correo electrónico/).fill(email);
    await page.getByLabel(/^Nombre/).fill('Persona E2E a11y');
    await page.getByRole('button', { name: 'Enviar invitación' }).click();
    await expect(page.getByText(`Invitación enviada a ${email}.`)).toBeVisible();
    const context = await browser.newContext({ storageState: SIGNED_OUT });
    const invitee = await context.newPage();
    await invitee.goto(await linkFromEmail(page, email, 'invitations/accept'));
    const password = 'an accessible e2e passphrase';
    await invitee.getByLabel(/^Contraseña nueva/).fill(password);
    await invitee.getByLabel(/^Repite la contraseña/).fill(password);
    await invitee.getByRole('button', { name: 'Crear contraseña' }).click();
    await expect(invitee.getByRole('img', { name: /Código QR/ })).toHaveAttribute('src', /^data:image\/svg/);

    // Each theme from a fresh load: switching live would catch colour transitions half-way.
    for (const colorScheme of ['light', 'dark'] as const) {
      await invitee.emulateMedia({ colorScheme });
      await invitee.reload();
      await expect(invitee.getByRole('img', { name: /Código QR/ })).toHaveAttribute(
        'src',
        /^data:image\/svg/,
      );
      expect(await axeViolations(invitee), `${colorScheme} theme`).toEqual([]);
    }
    await context.close();
  });
});
