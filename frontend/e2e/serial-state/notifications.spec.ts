import { execFile } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import { promisify } from 'node:util';
import type { Browser, Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from '../identity';
import { expect, test, waitForShell } from '../fixtures';

/**
 * Email notifications from end to end (UC-23; change add-notifications): preferences on the account
 * page, an order status email, the orders window and a milestone reminder sent by
 * `send-notifications`. It toggles the orders and the Norte order, which other specs read, so it runs
 * in the `serial-state` project and puts everything back. The stack must send every 2 s
 * (`NOTIFICATIONS_DISPATCH_INTERVAL_SECONDS=2`, as CI and docs/development.md set it).
 *
 * Emails are found by what is new in Mailpit since the action (a baseline of message ids), never by
 * comparing clocks; subjects are matched in the three languages, because other specs change the seeded
 * users' language.
 */

const MAILPIT_URL = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025';
const CURRENT_EDITION = '0193a500-0000-7000-8000-000000000002';
const NORTE_ORDER = '0193a700-0000-7000-8000-000000000003';
/** The seeded FiringChief of the `FIRING_CHIEF_STATE` (Norte). */
const JEFA_DOS = 'jefa.dos@polvorapp.example';
/** The other seeded FiringChief of Norte, who keeps every kind on. */
const JEFE_UNO = 'jefe.uno@polvorapp.example';
const ADMIN = 'admin@polvorapp.example';
const ALL_KINDS = ['LICENSE_DIGEST', 'ORDER_WINDOW', 'ORDER_STATUS', 'MILESTONE_REMINDER'];
const ORDER_WINDOW_SUBJECT =
  /^(Pedidos (abiertos|cerrados)|Comandes (obertes|tancades)|Orders (open|closed))/;
/** Longer than two dispatch passes plus the send, with room for a slow runner. */
const EMAIL_TIMEOUT = 45_000;

const run = promisify(execFile);

interface MailSummary {
  ID: string;
  Subject: string;
}

interface Email {
  id: string;
  subject: string;
  text: string;
}

/** The newest emails sent to `to` (ids and subjects only). */
async function summaries(page: Page, to: string): Promise<MailSummary[]> {
  const search = await page.request.get(`${MAILPIT_URL}/api/v1/search`, {
    params: { query: `to:"${to}"`, limit: 200 },
  });
  expect(search.ok(), 'Mailpit search').toBe(true);
  return ((await search.json()) as { messages: MailSummary[] }).messages;
}

/** The ids of the emails `to` has now: what a test compares against after its action. */
async function baseline(page: Page, to: string): Promise<Set<string>> {
  return new Set((await summaries(page, to)).map((message) => message.ID));
}

/** The emails to `to` that are not in `before` and whose subject matches, with their text. */
async function newEmails(page: Page, to: string, before: Set<string>, subject: RegExp): Promise<Email[]> {
  const matching = (await summaries(page, to)).filter(
    (message) => !before.has(message.ID) && subject.test(message.Subject),
  );
  return Promise.all(
    matching.map(async ({ ID, Subject }) => {
      const message = await page.request.get(`${MAILPIT_URL}/api/v1/message/${ID}`);
      expect(message.ok(), 'Mailpit message').toBe(true);
      const { Text } = (await message.json()) as { Text: string };
      return { id: ID, subject: Subject, text: Text };
    }),
  );
}

/** Waits for a new email to `to` whose subject matches `subject`, and returns it. */
async function waitForEmail(page: Page, to: string, subject: RegExp, before: Set<string>): Promise<Email> {
  let found: Email | undefined;
  await expect
    .poll(
      async () => {
        found = (await newEmails(page, to, before, subject))[0];
        return found !== undefined;
      },
      { message: `an email ${String(subject)} to ${to}`, timeout: EMAIL_TIMEOUT },
    )
    .toBe(true);
  if (!found) throw new Error(`No email ${String(subject)} to ${to}.`);
  return found;
}

async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

async function asFiringChief(browser: Browser) {
  const context = await browser.newContext({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES' });
  return { context, page: await context.newPage() };
}

async function setOrders(page: Page, open: boolean) {
  const edition = (await (await page.request.get(`/api/editions/${CURRENT_EDITION}`)).json()) as {
    version: number;
    ordersOpen: boolean;
  };
  if (edition.ordersOpen === open) return;
  const response = await page.request.post(`/api/editions/${CURRENT_EDITION}/orders`, {
    headers: await antiforgeryHeaders(page),
    data: { open, version: edition.version },
  });
  expect(response.ok(), `setting the orders ${open ? 'open' : 'closed'}`).toBe(true);
}

async function orderState(page: Page) {
  return (await (await page.request.get(`/api/comparsa-orders/${NORTE_ORDER}`)).json()) as {
    status: string;
    version: number;
  };
}

/** Every kind on again for the seeded FiringChief, except the digest the seed turns off. */
async function restorePreferences(chief: Page) {
  const response = await chief.request.put('/api/account/notification-preferences', {
    headers: await antiforgeryHeaders(chief),
    data: { kinds: ALL_KINDS.map((kind) => ({ kind, enabled: kind !== 'LICENSE_DIGEST' })) },
  });
  expect(response.ok(), 'restoring the notification preferences').toBe(true);
}

/** Runs every clean-up step even when one fails, then reports the first failure. */
async function cleanUp(...steps: (() => Promise<unknown>)[]): Promise<void> {
  const failures: unknown[] = [];
  for (const step of steps) {
    try {
      await step();
    } catch (error) {
      failures.push(error);
    }
  }
  if (failures.length > 0) throw failures[0];
}

test('a FiringChief turns off the order window emails and is not told when the orders toggle', async ({
  page,
  browser,
  axeViolations,
}) => {
  test.slow();
  const { context, page: chief } = await asFiringChief(browser);
  try {
    await chief.goto('/account');
    await waitForShell(chief);
    const section = chief.getByRole('region', { name: 'Avisos por correo' });
    await expect(section).toContainText('Apertura y cierre de pedidos');
    await section.getByRole('button', { name: 'Editar avisos por correo' }).click();
    const panel = chief.getByRole('dialog');
    await panel.getByRole('checkbox', { name: 'Apertura y cierre de pedidos' }).uncheck();
    expect(await axeViolations(chief, '[role=dialog]')).toEqual([]);
    await panel.getByRole('button', { name: 'Guardar cambios' }).click();
    await expect(chief.getByText('Cambios guardados').first()).toBeVisible();
    expect(await axeViolations(chief)).toEqual([]);

    // Saved for good: after a reload the box of that kind is still clear.
    await chief.reload();
    await waitForShell(chief);
    await chief.getByRole('button', { name: 'Editar avisos por correo' }).click();
    await expect(
      chief.getByRole('dialog').getByRole('checkbox', { name: 'Apertura y cierre de pedidos' }),
    ).not.toBeChecked();
    await chief.keyboard.press('Escape');

    const toJefeUno = await baseline(page, JEFE_UNO);
    const toJefaDos = await baseline(page, JEFA_DOS);
    await setOrders(page, false);
    await setOrders(page, true);

    // Both FiringChiefs' deliveries are worked out in the same pass: once Jefe Uno's email is in,
    // hers was skipped or would be in too. A short wait covers the rest of that pass.
    await waitForEmail(page, JEFE_UNO, /^(Pedidos abiertos|Comandes obertes|Orders open)/, toJefeUno);
    await page.waitForTimeout(3_000);
    expect(await newEmails(page, JEFA_DOS, toJefaDos, ORDER_WINDOW_SUBJECT)).toEqual([]);
  } finally {
    await cleanUp(
      () => setOrders(page, true),
      () => restorePreferences(chief),
      () => context.close(),
    );
  }
});

test('an Admin returns the Norte order and its FiringChief is emailed without the reason', async ({
  page,
  browser,
}) => {
  test.slow();
  const { context, page: chief } = await asFiringChief(browser);
  const reason = `Motivo E2E privado ${randomUUID()}`;
  try {
    let order = await orderState(page);
    if (order.status === 'DRAFT' || order.status === 'RETURNED') {
      const submitted = await chief.request.post(`/api/comparsa-orders/${NORTE_ORDER}/submit`, {
        headers: await antiforgeryHeaders(chief),
        data: { version: order.version, attestation: true },
      });
      expect(submitted.ok(), 'submitting the order first').toBe(true);
      order = await orderState(page);
    }

    const before = await baseline(page, JEFA_DOS);
    const returned = await page.request.post(`/api/comparsa-orders/${NORTE_ORDER}/return`, {
      headers: await antiforgeryHeaders(page),
      data: { version: order.version, reason },
    });
    expect(returned.ok(), 'returning the order').toBe(true);

    const email = await waitForEmail(
      page,
      JEFA_DOS,
      /^(Pedido devuelto|Comanda retornada|Order returned): Comparsa Sintética Norte/,
      before,
    );
    expect(email.text).toContain(`/orders/${NORTE_ORDER}`);
    expect(email.text).not.toContain(reason);
    expect(email.text).toContain('/account?section=notifications');
  } finally {
    await cleanUp(
      async () => {
        // As seeded: submitted by the FiringChief, with the attestation.
        const order = await orderState(page);
        if (order.status !== 'RETURNED') return;
        const submitted = await chief.request.post(`/api/comparsa-orders/${NORTE_ORDER}/submit`, {
          headers: await antiforgeryHeaders(chief),
          data: { version: order.version, attestation: true },
        });
        expect(submitted.ok(), 'submitting the order again after the test').toBe(true);
      },
      () => context.close(),
    );
  }
});

test('an Admin marks a milestone for a reminder and send-notifications emails it', async ({
  page,
  axeViolations,
}) => {
  // The UI steps, a cold `docker compose run` and the email.
  test.setTimeout(180_000);
  const title = `Hito E2E ${randomUUID().slice(0, 8)}`;
  const date = new Date(Date.now() + 3 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10);
  try {
    await page.goto(`/editions/${CURRENT_EDITION}`);
    await waitForShell(page);
    await page.getByRole('button', { name: 'Añadir hito' }).click();
    const panel = page.getByRole('dialog', { name: 'Nuevo hito' });
    await panel.getByLabel('Fecha').fill(date);
    await panel.getByRole('textbox', { name: 'Título' }).fill(title);
    await panel.getByRole('checkbox', { name: 'Enviar un recordatorio por correo' }).check();
    expect(await axeViolations(page, '[role=dialog]')).toEqual([]);
    await panel.getByRole('button', { name: 'Guardar cambios' }).click();
    const row = page.getByRole('row', { name: new RegExp(title) });
    await expect(row.getByRole('cell', { name: 'Sí' })).toBeVisible();

    // The command the operators run, from the repository root (compose.yaml and .env).
    try {
      await run('docker', ['compose', 'run', '--rm', '-T', 'api', 'send-notifications'], {
        cwd: path.resolve(import.meta.dirname, '..', '..', '..'),
        timeout: 90_000,
        // The API logs every SQL command in Development: far more than the 1 MB default.
        maxBuffer: 64 * 1024 * 1024,
      });
    } catch (error) {
      // Exit code 1 also means "a delivery to someone else failed": the email below is what counts.
      const { code, stderr } = error as { code?: number; stderr?: string };
      if (code !== 1) throw new Error(`send-notifications failed (${String(code)}): ${stderr ?? ''}`);
    }

    // The title is new, so any email with it is this one.
    const email = await waitForEmail(
      page,
      ADMIN,
      new RegExp(`^(Recordatorio|Recordatori|Reminder): ${title},`),
      new Set(),
    );
    expect(email.text).toContain(`/editions/${CURRENT_EDITION}`);
  } finally {
    // By title, so a failure before the milestone's id was known still removes it.
    const edition = (await (await page.request.get(`/api/editions/${CURRENT_EDITION}`)).json()) as {
      milestones: { id: string; title: string }[];
    };
    for (const milestone of edition.milestones.filter((m) => m.title === title)) {
      const removed = await page.request.delete(
        `/api/editions/${CURRENT_EDITION}/milestones/${milestone.id}`,
        {
          headers: await antiforgeryHeaders(page),
        },
      );
      expect(removed.ok(), 'removing the test milestone').toBe(true);
    }
  }
});
