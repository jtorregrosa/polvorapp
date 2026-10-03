import { FIRING_CHIEF_STATE, FIRING_CHIEF_UNO_STATE } from './identity';
import type { Locator } from '@playwright/test';
import { expect, test, waitForShell } from './fixtures';

/**
 * Comparsa orders (change add-comparsa-orders) against the seeded stack, read only: the current
 * edition has Norte submitted, Sur in draft and Este not prepared; last year's Norte and Sur orders
 * are validated, so "first year" is known (design D13). Changes to orders are in
 * `serial-state/order-review.spec.ts`. The billing summary (change add-billing-summary) uses the
 * seeded edition's prices: powder 48,00 € per kg, caps 3,75 € a box, rentals 25,00 € and 5,00 €.
 */

/**
 * Neither the page nor the billing table scrolls sideways: the table sits in an `overflow-x-auto`
 * container, so a table too wide would scroll inside it without widening the page.
 */
async function expectNoSidewaysScroll(billing: Locator) {
  const table = billing.getByRole('table');
  await expect(table).toBeVisible();
  expect(
    await table.evaluate((element) => {
      const box = element.parentElement;
      return box !== null && box.scrollWidth <= box.clientWidth;
    }),
  ).toBe(true);
  expect(
    await table.page().evaluate(() => {
      const root = document.documentElement;
      return root.scrollWidth <= root.clientWidth;
    }),
  ).toBe(true);
}

const PAST_NORTE_ORDER = '0193a700-0000-7000-8000-000000000001';
const CURRENT_NORTE_ORDER = '0193a700-0000-7000-8000-000000000003';
const CURRENT_SUR_ORDER = '0193a700-0000-7000-8000-000000000004';

test('the Admin sees the dashboard of the current edition', async ({ page, axeViolations }) => {
  await page.goto('/orders');
  await waitForShell(page);

  await expect(page.getByRole('heading', { level: 1, name: /^Pedidos de \d{4}$/ })).toBeVisible();
  const figures = page.getByRole('region', { name: 'Pedidos por estado' });
  await expect(figures).toBeVisible();
  await expect(page.getByRole('region', { name: 'Totales de la edición' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Comparsa Sintética Norte' })).toHaveAttribute(
    'href',
    `/orders/${CURRENT_NORTE_ORDER}`,
  );
  await expect(page.getByRole('link', { name: 'Comparsa Sintética Sur' })).toHaveAttribute(
    'href',
    `/orders/${CURRENT_SUR_ORDER}`,
  );
  await expect(
    page.getByRole('button', { name: 'Preparar pedido de Comparsa Sintética Este' }),
  ).toBeVisible();
  // Each comparsa's row (a table row, or a list item on a phone) says its order's status.
  const rowOf = (comparsa: string) =>
    page.locator('tr, li').filter({ hasText: comparsa }).filter({ hasNotText: 'Comparsas' }).first();
  await expect(rowOf('Comparsa Sintética Norte')).toContainText('Enviado');
  await expect(rowOf('Comparsa Sintética Sur')).toContainText('Borrador');
  await expect(rowOf('Comparsa Sintética Este')).toContainText('Sin preparar');
  // Each prepared order's amount, and the edition billing, provisional while Sur is a draft.
  await expect(rowOf('Comparsa Sintética Norte')).toContainText('€');
  await expect(rowOf('Comparsa Sintética Este')).not.toContainText('€');
  const billing = page.getByRole('region', { name: 'Resumen de pago de la edición' });
  await expect(billing).toContainText('Provisional');
  await expect(billing.getByRole('rowheader', { name: 'Total', exact: true })).toBeVisible();
  await expectNoSidewaysScroll(billing);
  expect(await axeViolations()).toEqual([]);
});

test.describe('as the seeded FiringChief of Norte', () => {
  test.use({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES' });

  test('"Orders" opens her single order, with the first-year flags and the warnings', async ({
    page,
    axeViolations,
  }) => {
    await page.goto('/orders');
    await waitForShell(page);

    await expect(page).toHaveURL(new RegExp(`/orders/${CURRENT_NORTE_ORDER}$`));
    await expect(
      page.getByRole('heading', { level: 1, name: 'Pedido de Comparsa Sintética Norte' }),
    ).toBeVisible();
    const entries = page.getByRole('region', { name: 'Líneas del pedido' });
    await expect(entries.getByText(/Primer año$/).first()).toBeVisible();
    await expect(entries.getByText('Menor de edad')).toBeVisible();
    await expect(page.getByRole('list', { name: 'Totales del pedido' })).toBeVisible();
    // Submitted, not validated: the billing summary is provisional, at the edition's prices.
    const billing = page.getByRole('region', { name: 'Resumen de pago' });
    await expect(billing).toContainText('Provisional');
    await expect(billing.getByRole('row', { name: /Pólvora/ })).toContainText('48,00');
    await expect(billing.getByRole('rowheader', { name: 'Total', exact: true })).toBeVisible();
    // On a phone (the mobile-360 project) the table must not scroll the page sideways.
    await expectNoSidewaysScroll(billing);
    expect(await axeViolations()).toEqual([]);
  });

  test('last year’s order is read-only', async ({ page }) => {
    await page.goto(`/orders/${PAST_NORTE_ORDER}`);
    await waitForShell(page);

    await expect(
      page.getByText('Los pedidos están cerrados: ya no puedes modificar este pedido.'),
    ).toBeVisible();
    await expect(page.getByRole('button', { name: /^Editar/ })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Enviar pedido' })).toHaveCount(0);
    await expect(page.getByText('Sintético Histórico, Arcabucero')).toBeVisible();
    // Shown from the entry's history copy (spec: Name kept after deletion).
    // A table row, or a list item on a phone.
    const historic = page.locator('tr, li').filter({ hasText: 'Sintético Histórico, Arcabucero' });
    await expect(historic).toContainText('DNI/NIE 00000091E');
    await expect(historic).toContainText('ID Unión 100091');
    await expect(historic).toContainText('Ya no está en el registro');
  });
});

test.describe('as the seeded FiringChief of Norte and Sur', () => {
  test.use({ storageState: FIRING_CHIEF_UNO_STATE, locale: 'es-ES' });

  test('Sur’s order shows its weapon lent to Norte', async ({ page, axeViolations }) => {
    await page.goto(`/orders/${CURRENT_SUR_ORDER}`);
    await waitForShell(page);

    const lent = page.getByRole('region', { name: 'Armas cedidas a otros' });
    await expect(lent).toContainText('Comparsa Sintética Norte');
    await expect(page.getByRole('region', { name: 'No están en el pedido' })).toContainText(
      'Sintético Nueve, Arcabucero',
    );
    expect(await axeViolations()).toEqual([]);
  });
});
