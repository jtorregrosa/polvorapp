import type { Browser, Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from '../identity';
import { expect, test, waitForShell } from '../fixtures';

/**
 * The review cycle of an order (UC-14, UC-15; change add-comparsa-orders, design D13). It changes
 * the seeded Norte order of the current edition, which every other spec reads, so it runs in the
 * `serial-state` project and puts the order back as seeded even when it fails midway.
 */

const NORTE_ID = '0193a100-0000-7000-8000-000000000001';
const NORTE_ORDER = '0193a700-0000-7000-8000-000000000003';
const LETTERS = 'TRWAGMYFPDXBNJZSQVHLCKE';
/** The external owner's synthetic DNI: a very low number, unlikely to be in use (BR-01 letter). */
const EXTERNAL_OWNER_DNI = `00000093${LETTERS[93 % LETTERS.length]}`;

interface OrderState {
  status: 'DRAFT' | 'SUBMITTED' | 'RETURNED' | 'VALIDATED';
  version: number;
  submission: { byAdmin: boolean } | null;
  entries: { id: string; version: number; arquebusier: { id: string | null } }[];
}

/** The seed's values of the two entries the cycle changes (OrderSeeder entries 8 and 10). */
const SEEDED_ENTRIES: Record<string, Record<string, unknown>> = {
  // Arcabucero Sintético Uno: his own trabuco, 2 kg, three boxes of normal caps, his own flask.
  '0193a300-0000-7000-8000-000000000001': {
    status: 'ACTIVE',
    powderKg: 2,
    capsBoxes: 3,
    capsType: 'NORMAL',
    weaponSource: 'OWNED',
    ownedWeaponId: '0193a400-0000-7000-8000-000000000001',
    rentalWeaponModelId: null,
    loan: null,
    flask: 'OWNED',
  },
  // Arcabucero Sintético Tres: a powder carrier, 2 kg, no weapon, a 2 kg rented flask.
  '0193a300-0000-7000-8000-000000000003': {
    status: 'ACTIVE',
    powderKg: 2,
    capsBoxes: 0,
    capsType: null,
    weaponSource: 'NONE',
    ownedWeaponId: null,
    rentalWeaponModelId: null,
    loan: null,
    flask: 'RENTAL_2KG',
  },
};

async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

async function orderState(page: Page): Promise<OrderState> {
  return (await (await page.request.get(`/api/comparsa-orders/${NORTE_ORDER}`)).json()) as OrderState;
}

/**
 * Puts the Norte order back as the seed has it, whatever the test left: the two entries the cycle
 * changes get their seeded values (an Admin's edit keeps the status), and the order is submitted
 * again by the FiringChief, with the attestation.
 */
test.afterEach(async ({ page, browser }) => {
  let order = await orderState(page);
  if (order.status === 'VALIDATED' || (order.status === 'SUBMITTED' && order.submission?.byAdmin === true)) {
    const returned = await page.request.post(`/api/comparsa-orders/${NORTE_ORDER}/return`, {
      headers: await antiforgeryHeaders(page),
      data: { version: order.version, reason: 'Devuelto por la prueba E2E.' },
    });
    expect(returned.ok(), 'returning the order after the test').toBe(true);
    order = await orderState(page);
  }
  for (const entry of order.entries) {
    const seeded = entry.arquebusier.id ? SEEDED_ENTRIES[entry.arquebusier.id] : undefined;
    if (!seeded) continue;
    const restored = await page.request.put(`/api/comparsa-orders/${NORTE_ORDER}/entries/${entry.id}`, {
      headers: await antiforgeryHeaders(page),
      data: { version: entry.version, ...seeded },
    });
    expect(restored.ok(), 'restoring a seeded entry after the test').toBe(true);
  }
  order = await orderState(page);
  if (order.status === 'SUBMITTED') return;
  const { context, page: chief } = await asFiringChief(browser);
  try {
    const submitted = await chief.request.post(`/api/comparsa-orders/${NORTE_ORDER}/submit`, {
      headers: await antiforgeryHeaders(chief),
      data: { version: order.version, attestation: true },
    });
    expect(submitted.ok(), 'submitting the order again after the test').toBe(true);
  } finally {
    await context.close();
  }
});

async function asFiringChief(browser: Browser, viewport?: { width: number; height: number }) {
  const context = await browser.newContext({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES', viewport });
  const page = await context.newPage();
  return { context, page };
}

async function openOrder(page: Page) {
  await page.goto(`/orders/${NORTE_ORDER}`);
  await waitForShell(page);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Pedido de Comparsa Sintética Norte' }),
  ).toBeVisible();
}

async function editEntry(page: Page, name: string) {
  await page.getByRole('button', { name: `Editar la línea de ${name}` }).click();
  const panel = page.getByRole('dialog', { name: `Línea de ${name}` });
  await expect(panel).toBeVisible();
  return panel;
}

async function saveEntry(page: Page) {
  await page.getByRole('dialog').getByRole('button', { name: 'Guardar cambios' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByText('Cambios guardados').first()).toBeVisible();
}

async function submitWithAttestation(page: Page) {
  await page.getByRole('button', { name: 'Enviar pedido' }).click();
  const dialog = page.getByRole('alertdialog');
  await dialog.getByRole('checkbox', { name: /Declaro que los arcabuceros/ }).check();
  await dialog.getByRole('button', { name: 'Enviar', exact: true }).click();
  await expect(page.getByText('Pedido enviado.')).toBeVisible();
}

test('a FiringChief submits, the Admin returns it, she fixes and resubmits, and the Admin validates', async ({
  page,
  browser,
}) => {
  // Two users, eleven page loads and six confirmed writes.
  test.slow();
  const { context, page: chief } = await asFiringChief(browser);
  try {
    await openOrder(chief);

    const tres = await editEntry(chief, 'Sintético Tres, Arcabucero');
    await tres.getByRole('radio', { name: /^Reserva/ }).click();
    await saveEntry(chief);
    await expect(chief.getByRole('row', { name: /Sintético Tres, Arcabucero/ })).toContainText('Reserva');

    const uno = await editEntry(chief, 'Sintético Uno, Arcabucero');
    await uno.getByRole('radio', { name: /^Cesión/ }).click();
    const change = uno.getByRole('button', { name: 'Cambiar el propietario' });
    if (await change.isVisible()) await change.click();
    await uno.getByRole('textbox', { name: 'DNI/NIE del propietario' }).fill(EXTERNAL_OWNER_DNI);
    await uno.getByRole('button', { name: 'Buscar propietario' }).click();
    await expect(uno.getByText(/No hay ningún arcabucero con este DNI\/NIE/)).toBeVisible();
    await uno.getByRole('textbox', { name: 'Nombre del propietario' }).fill('Propietario');
    await uno.getByRole('textbox', { name: 'Apellidos del propietario' }).fill('Externo Sintético E2E');
    await uno
      .getByRole('combobox', { name: 'Modelo del arma' })
      .selectOption({ label: 'TRABUCO CRISTIANO DIESTRO' });
    await uno.getByRole('textbox', { name: 'Número del arma' }).fill('E2E-0001');
    await uno.getByRole('textbox', { name: 'Número de la guía de pertenencia' }).fill('SINT-E2E-0001');
    await saveEntry(chief);
    await expect(chief.getByRole('row', { name: /Sintético Uno, Arcabucero/ })).toContainText(
      'Cesión de Externo Sintético E2E, Propietario (externo)',
    );

    await submitWithAttestation(chief);

    await openOrder(page);
    await page.getByRole('button', { name: 'Devolver' }).click();
    const returning = page.getByRole('alertdialog');
    await returning
      .getByRole('textbox', { name: 'Motivo de la devolución' })
      .fill('Revisad la línea de Sintético Tres.');
    await returning.getByRole('button', { name: 'Devolver' }).click();
    await expect(page.getByText('Pedido devuelto.')).toBeVisible();

    await openOrder(chief);
    await expect(chief.getByText('Devuelto por Admin Sintética')).toBeVisible();
    await expect(chief.getByRole('region', { name: 'Resumen de pago' })).toContainText('Provisional');
    await expect(chief.getByText('Revisad la línea de Sintético Tres.')).toBeVisible();
    const fix = await editEntry(chief, 'Sintético Tres, Arcabucero');
    await fix.getByRole('radio', { name: 'Activo' }).click();
    await saveEntry(chief);
    await submitWithAttestation(chief);

    await openOrder(page);
    await page.getByRole('button', { name: 'Validar' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Validar' }).click();
    await expect(page.getByText('Pedido validado.')).toBeVisible();
    await expect(page.getByRole('region', { name: 'Resumen de pago' })).toContainText('Definitivo');

    await openOrder(chief);
    await expect(chief.getByText('El pedido está validado: ya no puedes modificarlo.')).toBeVisible();
    await expect(chief.getByRole('button', { name: /^Editar/ })).toHaveCount(0);
    await expect(chief.getByRole('region', { name: 'Resumen de pago' })).toContainText('Definitivo');
  } finally {
    await context.close();
  }
});

test('on a phone the entry panel opens as a bottom sheet', async ({ browser }) => {
  const { context, page: chief } = await asFiringChief(browser, { width: 360, height: 740 });
  try {
    await openOrder(chief);

    const panel = await editEntry(chief, 'Sintético Cinco, Arcabucero');

    await expect(panel).toHaveAttribute('data-side', 'bottom');
  } finally {
    await context.close();
  }
});

test('deleting an arquebusier warns about, and removes, their entry in the open order', async ({
  page,
  browser,
}) => {
  const number = 70_000_000 + (Math.floor(Date.now() / 1000) % 1_000_000);
  const lastName = `Sintético Borrable ${number}`;
  const registered = await page.request.post('/api/arquebusiers', {
    headers: await antiforgeryHeaders(page),
    data: {
      comparsaId: NORTE_ID,
      federationId: number,
      nationalId: `${String(number).padStart(8, '0')}${LETTERS[number % LETTERS.length]}`,
      firstName: 'Arcabucero',
      lastName,
      birthDate: '1990-05-01',
      email: null,
      phone: null,
      gender: 'MALE',
      status: 'ACTIVE',
      trainingCompletedOn: null,
      license: null,
    },
  });
  expect(registered.status()).toBe(201);
  const { id } = (await registered.json()) as { id: string };
  const name = `${lastName}, Arcabucero`;

  const { context, page: chief } = await asFiringChief(browser);
  try {
    await openOrder(chief);
    await chief.getByRole('button', { name: `Añadir a ${name}` }).click();
    await expect(chief.getByText(`${name} se ha añadido al pedido.`)).toBeVisible();
    await expect(chief.getByRole('row', { name: new RegExp(lastName) })).toBeVisible();

    await chief.goto(`/arquebusiers/${id}`);
    await waitForShell(chief);
    await chief.getByRole('button', { name: 'Más acciones' }).click();
    await chief.getByRole('menuitem', { name: 'Eliminar arcabucero' }).click();
    const dialog = chief.getByRole('alertdialog');
    await expect(
      dialog.getByText(/Su línea se eliminará del pedido de \d{4} de Comparsa Sintética Norte/),
    ).toBeVisible();
    await dialog.getByRole('button', { name: 'Eliminar', exact: true }).click();
    await expect(chief).toHaveURL(/\/arquebusiers$/);

    await openOrder(chief);
    await expect(chief.getByText(name)).toHaveCount(0);
  } finally {
    await context.close();
    const cleanup = await page.request.delete(`/api/arquebusiers/${id}`, {
      headers: await antiforgeryHeaders(page),
    });
    expect([204, 404], 'deleting the synthetic arquebusier').toContain(cleanup.status());
  }
});
