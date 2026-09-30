import { randomUUID } from 'node:crypto';
import { expect, type Browser, type Page } from '@playwright/test';
import { Secret, TOTP } from 'otpauth';

/**
 * Identity helpers for the E2E suite: the seeded synthetic users (`docker compose run --rm
 * api-seed`), authenticator codes, and the emails caught by Mailpit. Everything here is
 * synthetic; the defaults are the local-only placeholders published in `.env.example`.
 */

const SEED_PASSWORD = process.env.SEED_USER_PASSWORD ?? 'local-only-seed-password'; // gitleaks:allow (public local placeholder)
const SEED_KEY = process.env.SEED_AUTHENTICATOR_KEY ?? 'JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP'; // gitleaks:allow (public local placeholder)
const MAILPIT_URL = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025';
const TOTP_PERIOD_SECONDS = 30;

export const ADMIN_STATE = 'e2e/.auth/admin.json';
export const FIRING_CHIEF_STATE = 'e2e/.auth/firing-chief.json';

/** A user who can sign in: email, password and authenticator key. */
export interface Account {
  email: string;
  password: string;
  secret: string;
  /** The last authenticator time step used; the API refuses a code twice (replay protection). */
  lastStep: number;
}

export const SEEDED = {
  admin: { email: 'admin@polvorapp.example', password: SEED_PASSWORD, secret: SEED_KEY, lastStep: 0 },
  firingChief: {
    email: 'jefa.dos@polvorapp.example',
    password: SEED_PASSWORD,
    secret: SEED_KEY,
    lastStep: 0,
  },
} satisfies Record<string, Account>;

/** Time steps on either side of now that the API accepts (clock drift tolerance). */
const ACCEPTED_DRIFT_STEPS = 1;

const currentStep = (): number => Math.floor(Date.now() / 1000 / TOTP_PERIOD_SECONDS);

const pause = (milliseconds: number): Promise<void> =>
  new Promise((resolve) => {
    setTimeout(resolve, milliseconds);
  });

/**
 * A code the API has not seen for this account. The API refuses a time step it already accepted
 * but allows one step ahead, so the next unused step is usually available at once; only a step
 * further ahead is waited for. Remembers the step it hands out.
 */
export async function nextCode(account: Account): Promise<string> {
  const step = Math.max(currentStep(), account.lastStep + 1);
  while (step > currentStep() + ACCEPTED_DRIFT_STEPS) {
    await pause(1_000);
  }
  account.lastStep = step;
  return new TOTP({ secret: Secret.fromBase32(account.secret), period: TOTP_PERIOD_SECONDS }).generate({
    timestamp: step * TOTP_PERIOD_SECONDS * 1000,
  });
}

/**
 * Types an authenticator code; when the API refuses the code (another run used this time step for
 * the same seeded user), tries once more with the next step. Any other failure stops at once.
 */
async function enterCode(page: Page, account: Account, field: RegExp, submit: RegExp): Promise<void> {
  for (let attempt = 0; attempt < 2; attempt += 1) {
    await page.getByLabel(field).fill(await nextCode(account));
    const [response] = await Promise.all([
      page.waitForResponse(
        (candidate) =>
          candidate.request().method() === 'POST' &&
          /^\/api\/auth\/(login\/second-factor|enrolment)$/.test(new URL(candidate.url()).pathname),
      ),
      page.getByRole('button', { name: submit }).click(),
    ]);
    if (response.ok()) return;
    const { code } = (await response.json().catch(() => ({}))) as { code?: string };
    if (code !== 'auth.invalidCode') {
      throw new Error(`The code step for ${account.email} failed: ${response.status()} ${code ?? ''}`);
    }
  }
  throw new Error(`The authenticator code for ${account.email} was refused twice`);
}

/** Signs in through the UI (password, then the authenticator code) and waits for the shell. */
export async function signIn(page: Page, account: Account, { rememberDevice = false } = {}): Promise<void> {
  await page.goto('/login');
  await page.getByLabel(/^(Correo electrónico|Correu electrònic|Email)/).fill(account.email);
  await page.getByLabel(/^(Contraseña|Contrasenya|Password)/).fill(account.password);
  await page.getByRole('button', { name: /^(Continuar|Continua|Continue)$/ }).click();
  await expect(page).toHaveURL(/\/login\/second-factor/);
  if (rememberDevice) {
    await page.getByRole('checkbox', { name: /30/ }).check();
  }
  await enterCode(page, account, /^(Código|Codi|Code)/, /^(Verificar|Verifica|Verify)$/);
  await expect(page.getByRole('button', { name: /^(Menú de|Menú de|Menu for) / })).toBeVisible();
}

interface MailpitSummary {
  ID: string;
}

/** The link to `path` in the newest email sent to `to` that has one, waiting for it to arrive. */
export async function linkFromEmail(page: Page, to: string, path: string): Promise<string> {
  const pattern = new RegExp(`https?://[^\\s<>")]+/${path}\\?[^\\s<>")]+`);
  let link: string | undefined;
  await expect
    .poll(
      async () => {
        const search = await page.request.get(`${MAILPIT_URL}/api/v1/search`, {
          params: { query: `to:"${to}"` },
        });
        expect(search.ok(), 'Mailpit search').toBe(true);
        const { messages } = (await search.json()) as { messages: MailpitSummary[] };
        // Newest first: the latest link wins, and later unrelated emails are skipped.
        for (const { ID } of messages) {
          const message = await page.request.get(`${MAILPIT_URL}/api/v1/message/${ID}`);
          expect(message.ok(), 'Mailpit message').toBe(true);
          const { Text } = (await message.json()) as { Text: string };
          link = pattern.exec(Text)?.[0];
          if (link) return link;
        }
        return undefined;
      },
      { message: `an email to ${to} with a link to /${path}`, timeout: 15_000 },
    )
    .toBeTruthy();
  return new URL(link ?? '').pathname + new URL(link ?? '').search;
}

/** A unique synthetic address, so each run and each test gets its own user. */
export const uniqueEmail = (label: string): string => `e2e-${label}-${randomUUID()}@polvorapp.example`;

/** A user created by {@link inviteFiringChief}. */
export interface InvitedAccount extends Account {
  name: string;
  /** The user's page for Admins. */
  detailPath: string;
}

/**
 * An Admin invites a new FiringChief through the UI; the invitee accepts, enrols an
 * authenticator and saves the recovery codes. Returns the new account, signed out.
 */
export async function inviteFiringChief(
  browser: Browser,
  admin: Page,
  label: string,
): Promise<InvitedAccount> {
  const email = uniqueEmail(label);
  const name = `Persona E2E ${label} ${randomUUID().slice(0, 8)}`;
  const password = `e2e passphrase ${randomUUID()}`;
  await admin.goto('/users/new');
  await admin.getByLabel(/^Correo electrónico/).fill(email);
  await admin.getByLabel(/^Nombre/).fill(name);
  await admin.getByRole('button', { name: 'Enviar invitación' }).click();
  await expect(admin.getByText(`Invitación enviada a ${email}.`)).toBeVisible();
  const detailPath = new URL(admin.url()).pathname;

  const link = await linkFromEmail(admin, email, 'invitations/accept');
  const context = await browser.newContext({ storageState: { cookies: [], origins: [] } });
  const page = await context.newPage();
  await page.goto(link);
  await page.getByLabel(/^Contraseña nueva/).fill(password);
  await page.getByLabel(/^Repite la contraseña/).fill(password);
  await page.getByRole('button', { name: 'Crear contraseña' }).click();

  // Shown in groups of four, in lower case, for typing by hand: back to plain Base32.
  const shownKey = (await page.locator('code').first().textContent()) ?? '';
  const secret = shownKey.replace(/\s/g, '').toUpperCase();
  const account: InvitedAccount = { email, password, secret, lastStep: 0, name, detailPath };
  await enterCode(page, account, /^Código de la aplicación/, /^Activar$/);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Guarda tus códigos de recuperación' }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Ya los he guardado' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Bienvenida' })).toBeVisible();
  await context.close();
  return account;
}
