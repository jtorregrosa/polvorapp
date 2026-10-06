import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import type { OverviewResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { sortableColumns } from '@/test/table';
import { billingOf } from '@/features/billing/test-data';
import { ADMIN_OVERVIEW, CHIEF_OVERVIEW, NO_EDITION_OVERVIEW, NORTE_ORDER } from '../test-data';

const ORIGINAL_WIDTH = window.innerWidth;

function setViewportWidth(width: number) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: width });
}

function overview(response: OverviewResponse) {
  server.use(mock.get('/api/comparsa-orders/overview', () => HttpResponse.json(response)));
}

describe('OrdersOverviewPage (spec: Orders screens, Order totals and dashboard (UC-16))', () => {
  afterEach(() => {
    setViewportWidth(ORIGINAL_WIDTH);
  });

  it('shows an Admin the orders by status, the edition totals by model and every comparsa', async () => {
    overview(ADMIN_OVERVIEW);
    await renderApp('/orders', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByRole('heading', { level: 1, name: 'Pedidos de 2031' })).toBeInTheDocument();
    const figures = screen.getByRole('group', { name: 'Pedidos por estado' });
    expect(within(figures).getByRole('button', { name: '1 Sin preparar' })).toBeInTheDocument();
    expect(within(figures).getByRole('button', { name: '1 Enviados' })).toBeInTheDocument();
    expect(within(figures).getByRole('button', { name: '1 Validados' })).toBeInTheDocument();
    // The orders come before the edition's totals (UI audit).
    expect(
      screen
        .getByRole('table', { name: 'Pedidos de las comparsas' })
        .compareDocumentPosition(screen.getByRole('region', { name: 'Totales de la edición' })),
    ).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    const rentals = screen.getByRole('table', { name: 'Armas de alquiler por modelo' });
    expect(
      within(rentals).getByRole('rowheader', { name: 'ARCABUZ MORO DIESTRO' }).closest('tr'),
    ).toHaveTextContent('2');
    const totals = screen.getByRole('region', { name: 'Totales de la edición' });
    expect(totals).toHaveTextContent('8 kg');
    const table = screen.getByRole('table', { name: 'Pedidos de las comparsas' });
    expect(within(table).getByRole('link', { name: 'Comparsa Sintética Norte' })).toHaveAttribute(
      'href',
      `/orders/${NORTE_ORDER.id}`,
    );
    expect(within(table).getByText('Comparsa Sintética Este').closest('tr')).toHaveTextContent(
      'Sin preparar',
    );
    expect(within(table).getByText('Comparsa Sintética Sur').closest('tr')).toHaveTextContent('Enviado');
  });

  it('filters the orders by the statuses chosen in the counters, and says how many remain', async () => {
    const user = userEvent.setup();
    overview(ADMIN_OVERVIEW);
    await renderApp('/orders', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('link', { name: 'Comparsa Sintética Norte' });
    const table = screen.getByRole('table', { name: 'Pedidos de las comparsas' });
    const rowsShown = () =>
      within(table)
        .getAllByRole('rowheader')
        .map((cell) => cell.textContent);
    const all = rowsShown();

    await user.click(screen.getByRole('button', { name: '1 Sin preparar' }));

    expect(screen.getByRole('button', { name: '1 Sin preparar' })).toHaveAttribute('aria-pressed', 'true');
    expect(rowsShown()).toEqual(['Comparsa Sintética Este']);
    expect(screen.getByText('1 comparsa')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: '1 Enviados' }));
    expect(rowsShown()).toEqual(['Comparsa Sintética Este', 'Comparsa Sintética Sur']);

    await user.click(screen.getByRole('button', { name: '1 Sin preparar' }));
    await user.click(screen.getByRole('button', { name: '1 Enviados' }));
    expect(rowsShown()).toEqual(all);
    // Clearing the last counter is announced too.
    expect(screen.getByText(`${String(all.length)} comparsas`)).toHaveClass('sr-only');
  });

  it('shows each prepared order’s amount and, for an Admin, the edition billing (spec: Billing screens)', async () => {
    overview(ADMIN_OVERVIEW);
    await renderApp('/orders', { session: SYNTHETIC_ADMIN });

    const table = await screen.findByRole('table', { name: 'Pedidos de las comparsas' });
    const norte = (await within(table).findByText('Comparsa Sintética Norte')).closest('tr') as HTMLElement;
    expect(within(table).getByRole('columnheader', { name: /^Importe,/ })).toBeInTheDocument();
    expect(within(table).getByRole('columnheader', { name: /^Estado del importe,/ })).toBeInTheDocument();
    // The amount and whether it is final are separate columns.
    const amount = within(norte)
      .getByText(/^335,00\s€$/)
      .closest('td') as HTMLElement;
    const state = within(norte).getByText('Definitivo').closest('td') as HTMLElement;
    expect(amount).not.toBe(state);
    const sur = within(table).getByText('Comparsa Sintética Sur').closest('tr') as HTMLElement;
    expect(within(sur).getByText(/^225,00\s€$/)).toBeInTheDocument();
    expect(within(sur).getByText('Provisional')).toBeInTheDocument();
    const este = within(table).getByText('Comparsa Sintética Este').closest('tr') as HTMLElement;
    expect(within(este).queryByText(/€/)).not.toBeInTheDocument();
    const billing = screen.getByRole('region', { name: 'Resumen de pago de la edición' });
    expect(within(billing).getByRole('cell', { name: /^560,00\s€$/ })).toBeInTheDocument();
    const totals = screen.getByRole('region', { name: 'Totales de la edición' });
    expect(totals.compareDocumentPosition(billing) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('sorts the orders by any column but the action', async () => {
    overview(ADMIN_OVERVIEW);
    await renderApp('/orders', { session: SYNTHETIC_ADMIN });
    const table = await screen.findByRole('table', { name: 'Pedidos de las comparsas' });
    await within(table).findByText('Comparsa Sintética Norte');

    expect(sortableColumns(table)).toEqual([
      'Comparsa',
      'Estado',
      'Totales',
      'Importe',
      'Estado del importe',
    ]);
  });

  it('shows a FiringChief their orders’ amounts but no edition billing', async () => {
    overview(CHIEF_OVERVIEW);
    await renderApp('/orders', { session: SYNTHETIC_FIRING_CHIEF });

    const table = await screen.findByRole('table', { name: 'Pedidos de las comparsas' });
    const norte = (await within(table).findByText('Comparsa Sintética Norte')).closest('tr') as HTMLElement;
    expect(within(norte).getByText(/^335,00\s€$/)).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Resumen de pago de la edición' })).not.toBeInTheDocument();
  });

  it('says a price is missing instead of an amount', async () => {
    const missing = billingOf(
      { powderKg: 3, capsBoxes: 0, weaponRentals: 2, flaskRentals: 0 },
      'PROVISIONAL',
      ['WEAPON_RENTAL'],
    );
    overview({
      ...ADMIN_OVERVIEW,
      rows: ADMIN_OVERVIEW.rows.map((row) => (row.billing ? { ...row, billing: missing } : row)),
      editionBilling: missing,
    });
    await renderApp('/orders', { session: SYNTHETIC_ADMIN });

    const table = await screen.findByRole('table', { name: 'Pedidos de las comparsas' });
    const sur = (await within(table).findByText('Comparsa Sintética Sur')).closest('tr') as HTMLElement;
    expect(within(sur).getByText('Falta un precio')).toBeInTheDocument();
    const billing = screen.getByRole('region', { name: 'Resumen de pago de la edición' });
    expect(
      within(billing).getByText(/^Falta un precio en la edición: alquiler de arma\./),
    ).toBeInTheDocument();
  });

  it('shows the amount on a phone too', async () => {
    setViewportWidth(360);
    overview(ADMIN_OVERVIEW);
    await renderApp('/orders', { session: SYNTHETIC_ADMIN });

    await screen.findByRole('link', { name: 'Comparsa Sintética Norte' });
    expect(screen.getAllByText(/^335,00\s€$/)).toHaveLength(1);
    expect(screen.getAllByText('Importe:').length).toBeGreaterThan(0);
  });

  it('links an Admin to the exports of the edition (spec: Exports screens)', async () => {
    overview(ADMIN_OVERVIEW);
    await renderApp('/orders', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByRole('link', { name: 'Exportaciones' })).toHaveAttribute(
      'href',
      `/editions/${ADMIN_OVERVIEW.edition?.id}/exports`,
    );
  });

  it('shows a FiringChief no link to the exports', async () => {
    overview(CHIEF_OVERVIEW);
    await renderApp('/orders', { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('table', { name: 'Pedidos de las comparsas' });
    expect(screen.queryByRole('link', { name: 'Exportaciones' })).not.toBeInTheDocument();
  });

  it('lists a FiringChief’s comparsas with "Prepare order", without Federation figures', async () => {
    overview(CHIEF_OVERVIEW);
    await renderApp('/orders', { session: SYNTHETIC_FIRING_CHIEF });

    const table = await screen.findByRole('table', { name: 'Pedidos de las comparsas' });
    expect((await within(table).findByText('Comparsa Sintética Sur')).closest('tr')).toHaveTextContent(
      'Sin preparar',
    );
    expect(
      within(table).getByRole('button', { name: 'Preparar pedido de Comparsa Sintética Sur' }),
    ).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Pedidos por estado' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Totales de la edición' })).not.toBeInTheDocument();
  });

  it('prepares an order and opens it', async () => {
    const user = userEvent.setup();
    overview(CHIEF_OVERVIEW);
    const prepared = { ...NORTE_ORDER, id: '00000000-0000-4000-8000-000000000799' };
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(prepared, { status: 201 }));
    server.use(
      mock.post('/api/comparsa-orders', resolver),
      mock.get(`/api/comparsa-orders/${prepared.id}`, () => HttpResponse.json(prepared)),
    );
    const app = await renderApp('/orders', { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(
      await screen.findByRole('button', { name: 'Preparar pedido de Comparsa Sintética Sur' }),
    );

    await waitFor(() => {
      expect(app.location()).toBe(`/orders/${prepared.id}`);
    });
    expect(bodies).toEqual([
      { editionId: CHIEF_OVERVIEW.edition?.id, comparsaId: CHIEF_OVERVIEW.rows[1]?.comparsa.id },
    ]);
  });

  it('says why an order could not be prepared', async () => {
    const user = userEvent.setup();
    overview(CHIEF_OVERVIEW);
    server.use(mock.post('/api/comparsa-orders', () => problem(409, 'orders.closed')));
    await renderApp('/orders', { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(
      await screen.findByRole('button', { name: 'Preparar pedido de Comparsa Sintética Sur' }),
    );

    expect(
      await screen.findByText('Los pedidos están cerrados: ya no se puede modificar.'),
    ).toBeInTheDocument();
  });

  it('refreshes the rows after a refused preparation', async () => {
    const user = userEvent.setup();
    let loads = 0;
    server.use(
      mock.get('/api/comparsa-orders/overview', () => {
        loads += 1;
        return HttpResponse.json(CHIEF_OVERVIEW);
      }),
      mock.post('/api/comparsa-orders', () => problem(409, 'orders.alreadyPrepared')),
    );
    await renderApp('/orders', { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(
      await screen.findByRole('button', { name: 'Preparar pedido de Comparsa Sintética Sur' }),
    );

    await waitFor(() => {
      expect(loads).toBeGreaterThan(1);
    });
  });

  it('stays on an edition’s own overview even with a single prepared order', async () => {
    overview({ ...CHIEF_OVERVIEW, rows: CHIEF_OVERVIEW.rows.slice(0, 1) });
    const app = await renderApp(`/editions/${CHIEF_OVERVIEW.edition?.id ?? ''}/orders`, {
      session: SYNTHETIC_FIRING_CHIEF,
    });

    expect(await screen.findByRole('table', { name: 'Pedidos de las comparsas' })).toBeInTheDocument();
    expect(app.location()).toBe(`/editions/${CHIEF_OVERVIEW.edition?.id ?? ''}/orders`);
  });

  it('takes a FiringChief with a single prepared order straight to it', async () => {
    overview({ ...CHIEF_OVERVIEW, rows: CHIEF_OVERVIEW.rows.slice(0, 1) });
    server.use(mock.get(`/api/comparsa-orders/${NORTE_ORDER.id}`, () => HttpResponse.json(NORTE_ORDER)));
    const app = await renderApp('/orders', { session: SYNTHETIC_FIRING_CHIEF });

    await waitFor(() => {
      expect(app.location()).toBe(`/orders/${NORTE_ORDER.id}`);
    });
  });

  it('says when no edition is in progress and links to the editions', async () => {
    overview(NO_EDITION_OVERVIEW);
    await renderApp('/orders', { session: SYNTHETIC_FIRING_CHIEF });

    expect(
      await screen.findByRole('heading', { name: 'No hay ninguna edición en curso' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver las ediciones' })).toHaveAttribute('href', '/editions');
  });

  it.each([
    [true, 'Pedidos abiertos'],
    [false, 'Pedidos cerrados'],
  ])('says whether the orders are open (%s)', async (ordersOpen, text) => {
    overview({
      ...ADMIN_OVERVIEW,
      edition: ADMIN_OVERVIEW.edition && { ...ADMIN_OVERVIEW.edition, ordersOpen },
    });
    await renderApp('/orders', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByText(text)).toBeInTheDocument();
  });

  it('shows another edition’s orders, asking for it by id', async () => {
    let asked: string | null = null;
    server.use(
      mock.get('/api/comparsa-orders/overview', ({ request }) => {
        asked = new URL(request.url).searchParams.get('editionId');
        return HttpResponse.json({
          ...ADMIN_OVERVIEW,
          edition: { id: 'e-2030', year: 2030, status: 'CLOSED', ordersOpen: false },
        });
      }),
    );
    await renderApp('/editions/e-2030/orders', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByRole('heading', { level: 1, name: 'Pedidos de 2030' })).toBeInTheDocument();
    expect(asked).toBe('e-2030');
  });

  it('shows the not-found page for an edition it cannot see', async () => {
    server.use(mock.get('/api/comparsa-orders/overview', () => problem(404, 'orders.notFound')));
    await renderApp('/editions/e-2032/orders', { session: SYNTHETIC_FIRING_CHIEF });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Página no encontrada' }),
    ).toBeInTheDocument();
  });

  it('says the orders could not be loaded and retries', async () => {
    const user = userEvent.setup();
    let calls = 0;
    server.use(
      mock.get('/api/comparsa-orders/overview', () => {
        calls += 1;
        return calls === 1 ? problem(500, 'unexpected') : HttpResponse.json(ADMIN_OVERVIEW);
      }),
    );
    await renderApp('/orders', { session: SYNTHETIC_ADMIN });

    await user.click(await screen.findByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByRole('table', { name: 'Pedidos de las comparsas' })).toBeInTheDocument();
  });

  it('stacks the comparsas on a phone', async () => {
    setViewportWidth(360);
    overview(ADMIN_OVERVIEW);
    await renderApp('/orders', { session: SYNTHETIC_ADMIN });

    const link = await screen.findByRole('link', { name: 'Comparsa Sintética Norte' });
    expect(link.closest('table')).toBeNull();
  });

  it.each([
    ['Admin', SYNTHETIC_ADMIN, ADMIN_OVERVIEW],
    ['FiringChief', SYNTHETIC_FIRING_CHIEF, CHIEF_OVERVIEW],
  ])(
    'has no accessibility violations for an %s',
    async (_role, session, response) => {
      overview(response);
      const { container } = await renderApp('/orders', { session });
      await screen.findByRole('table', { name: 'Pedidos de las comparsas' });

      // Once the page is shown: axe is slow under load (coverage), and a retry loop would time out.

      expect(await axeViolations(container)).toEqual([]);
    },
    20_000,
  );
});
