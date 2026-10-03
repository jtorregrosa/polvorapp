import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import type { OrderResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { NORTE_ORDER } from '../test-data';

const ORIGINAL_WIDTH = window.innerWidth;

function setViewportWidth(width: number) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: width });
}

function serve(order: OrderResponse) {
  server.use(
    mock.get(`/api/comparsa-orders/${order.id}`, () => HttpResponse.json(order)),
    mock.get(`/api/comparsas/${order.comparsa.id}`, () =>
      HttpResponse.json({ ...order.comparsa, active: true, logo: null, firingChiefs: [], version: 1 }),
    ),
  );
}

async function open(order: OrderResponse = NORTE_ORDER, session = SYNTHETIC_FIRING_CHIEF) {
  serve(order);
  const app = await renderApp(`/orders/${order.id}`, { session });
  await screen.findByRole('heading', { level: 1, name: 'Pedido de Comparsa Sintética Norte' });
  return app;
}

describe('OrderPage (spec: Orders screens)', () => {
  afterEach(() => {
    setViewportWidth(ORIGINAL_WIDTH);
  });

  it('shows the comparsa, the edition, the statuses and the totals', async () => {
    await open();

    expect(screen.getByText('Fiestas 2031')).toBeInTheDocument();
    expect(screen.getByText('Borrador')).toBeInTheDocument();
    expect(screen.getByText('Pedidos abiertos')).toBeInTheDocument();
    const facts = screen.getByRole('list', { name: 'Totales del pedido' });
    expect(facts).toHaveTextContent('Activos3');
    expect(facts).toHaveTextContent('5 kg');
  });

  it('says why a FiringChief cannot edit it', async () => {
    await open({
      ...NORTE_ORDER,
      canEdit: false,
      readOnlyReason: 'ordersClosed',
      edition: { ...NORTE_ORDER.edition, ordersOpen: false },
    });

    expect(
      screen.getByText('Los pedidos están cerrados: ya no puedes modificar este pedido.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Editar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Añadir/ })).not.toBeInTheDocument();
  });

  it('shows the return reason while the order is returned', async () => {
    await open({
      ...NORTE_ORDER,
      status: 'RETURNED',
      review: {
        at: '2031-01-20T10:00:00Z',
        byName: 'Admin Sintética',
        returnReason: 'Revisad la cantimplora.\nGracias.',
      },
    });

    expect(screen.getByText('Devuelto por Admin Sintética')).toBeInTheDocument();
    expect(screen.getByText(/Revisad la cantimplora/)).toHaveTextContent('Revisad la cantimplora. Gracias.');
  });

  it('warns about the active entries with compliance warnings and links to them', async () => {
    await open();

    const link = screen.getByRole('link', { name: '1 arcabucero activo con avisos' });
    expect(link).toHaveAttribute('href', '#entries');
  });

  it('lists the entries that block the submission', async () => {
    const blocked = {
      ...NORTE_ORDER,
      entries: NORTE_ORDER.entries.map((entry, index) =>
        index === 0 ? { ...entry, issues: ['ownedWeaponMissing'] } : entry,
      ),
    };
    await open(blocked);

    const alert = screen.getByText('Hay líneas que revisar antes de enviar el pedido:').closest('div');
    expect(alert).toHaveTextContent('Sintético Uno, Arcabucero: el arma propia ya no está en el registro');
  });

  it('shows each entry with its first-year flag, warnings and weapon, and an "Edit" action', async () => {
    await open();

    const table = screen.getByRole('table', { name: 'Líneas del pedido' });
    const tres = within(table).getByText('Sintético Tres, Arcabucero').closest('tr');
    expect(tres).toHaveTextContent('Primer año');
    expect(tres).toHaveTextContent('Sin curso');
    expect(tres).toHaveTextContent('Alquiler: TRABUCO CRISTIANO DIESTRO');
    const cinco = within(table).getByText('Sintético Cinco, Arcabucero').closest('tr');
    expect(cinco).toHaveTextContent('Cesión de Sintética Seis, Arcabucera (Comparsa Sintética Sur)');
    expect(
      within(table).getByRole('button', { name: 'Editar la línea de Sintético Uno, Arcabucero' }),
    ).toBeInTheDocument();
  });

  it('shows each arquebusier’s DNI/NIE and ID Unión under the name', async () => {
    await open();

    const table = screen.getByRole('table', { name: 'Líneas del pedido' });
    const tres = within(table).getByRole('rowheader', { name: /^Sintético Tres, Arcabucero/ });
    expect(tres).toHaveTextContent('DNI/NIE 00000003A');
    expect(tres).toHaveTextContent('ID Unión 100003');
    expect(tres).toHaveTextContent('Primer año');
    // Dots are only seen; a screen reader hears a pause between the notes instead.
    expect(tres).toHaveAccessibleName(/DNI\/NIE 00000003A,\s*ID Unión 100003,\s*Primer año$/);
  });

  it('marks entries no longer in the registry, with the DNI/NIE of their copy, and offers no remove action', async () => {
    await open();

    const table = screen.getByRole('table', { name: 'Líneas del pedido' });
    const historic = within(table).getByRole('rowheader', { name: /^Sintético Histórico, Arcabucero/ });
    expect(historic).toHaveTextContent('DNI/NIE 00000091E');
    expect(historic).toHaveTextContent('ID Unión 100091');
    expect(historic).toHaveTextContent('Ya no está en el registro');
    expect(within(table).queryByRole('button', { name: /Quitar|Eliminar/ })).not.toBeInTheDocument();
  });

  it.each([
    ['without a DNI/NIE', { nationalId: null }, /Arcabucero\s*ID Unión 100091,\s*Ya no está en el registro$/],
    [
      'without an ID Unión',
      { federationId: null },
      /Arcabucero\s*DNI\/NIE 00000091E,\s*Ya no está en el registro$/,
    ],
    ['with both erased', { nationalId: null, federationId: null }, /Arcabucero\s*Ya no está en el registro$/],
  ])('shows a history copy %s without gaps', async (_case, erased, notes) => {
    const historic = NORTE_ORDER.entries[3];
    if (!historic) throw new Error('The fixture has a history entry.');
    await open({
      ...NORTE_ORDER,
      entries: [{ ...historic, arquebusier: { ...historic.arquebusier, ...erased } }],
    });

    const table = screen.getByRole('table', { name: 'Líneas del pedido' });
    expect(
      within(table).getByRole('rowheader', { name: /^Sintético Histórico, Arcabucero/ }),
    ).toHaveAccessibleName(notes);
  });

  it('shows no second line for an arquebusier with nothing to note', async () => {
    const [uno] = NORTE_ORDER.entries;
    if (!uno) throw new Error('The fixture has entries.');
    await open({
      ...NORTE_ORDER,
      entries: [{ ...uno, arquebusier: { ...uno.arquebusier, nationalId: null, federationId: null } }],
    });

    const table = screen.getByRole('table', { name: 'Líneas del pedido' });
    expect(within(table).getByRole('rowheader', { name: 'Sintético Uno, Arcabucero' })).toHaveTextContent(
      /^Sintético Uno, Arcabucero$/,
    );
  });

  it.each([
    [
      'with its ownership guide',
      'SINT-0001',
      'Propia: TRABUCO CRISTIANO DIESTRO 1001, guía SINT-0001 (ya no está en el registro)',
    ],
    [
      'without a guide once erased',
      null,
      'Propia: TRABUCO CRISTIANO DIESTRO 1001 (ya no está en el registro)',
    ],
  ])('shows an owned weapon no longer in the registry %s', async (_case, guide, weapon) => {
    const [uno, ...others] = NORTE_ORDER.entries;
    if (!uno?.ownedWeapon) throw new Error('The fixture’s first entry has an owned weapon.');
    await open({
      ...NORTE_ORDER,
      entries: [
        { ...uno, ownedWeapon: { ...uno.ownedWeapon, id: null, ownershipGuideNumber: guide, removed: true } },
        ...others,
      ],
    });

    const table = screen.getByRole('table', { name: 'Líneas del pedido' });
    expect(within(table).getByText('Sintético Uno, Arcabucero').closest('tr')).toHaveTextContent(weapon);
  });

  it('says when an Admin submitted it on the comparsa’s behalf', async () => {
    await open({
      ...NORTE_ORDER,
      status: 'SUBMITTED',
      submission: { at: '2031-01-18T12:00:00Z', byName: 'Admin Sintética', attested: false, byAdmin: true },
    });

    expect(screen.getByText(/Admin Sintética lo envió en nombre de la comparsa/)).toBeInTheDocument();
  });

  it('adds an arquebusier who is not in the order', async () => {
    const user = userEvent.setup();
    const added: OrderResponse = { ...NORTE_ORDER, version: 13, notInOrder: [] };
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(added));
    server.use(mock.post(`/api/comparsa-orders/${NORTE_ORDER.id}/entries`, resolver));
    await open();

    const section = screen.getByRole('region', { name: 'No están en el pedido' });
    await user.click(within(section).getByRole('button', { name: 'Añadir a Sintético Nueve, Arcabucero' }));

    await waitFor(() => {
      expect(bodies).toEqual([{ arquebusierId: NORTE_ORDER.notInOrder[0]?.arquebusierId }]);
    });
    expect(
      await screen.findByText('Sintético Nueve, Arcabucero se ha añadido al pedido.'),
    ).toBeInTheDocument();
  });

  it('shows the weapons lent to others', async () => {
    await open();

    const section = screen.getByRole('region', { name: 'Armas cedidas a otros' });
    expect(section).toHaveTextContent('TRABUCO CRISTIANO DIESTRO 1001');
    expect(section).toHaveTextContent('Sintética Siete, Arcabucera (Comparsa Sintética Sur)');
  });

  it('stacks the entries on a phone', async () => {
    setViewportWidth(360);
    await open();

    expect(screen.getByText('Sintético Uno, Arcabucero').closest('table')).toBeNull();
    expect(screen.getByText(/DNI\/NIE 00000001R/)).toHaveTextContent('ID Unión 100001');
    expect(screen.getByText(/DNI\/NIE 00000091E/)).toHaveTextContent('Ya no está en el registro');
  });

  it('shows the not-found page for an order it cannot see', async () => {
    server.use(mock.get('/api/comparsa-orders/unknown', () => problem(404, 'orders.notFound')));
    await renderApp('/orders/unknown', { session: SYNTHETIC_FIRING_CHIEF });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Página no encontrada' }),
    ).toBeInTheDocument();
  });

  it.each([
    ['FiringChief', SYNTHETIC_FIRING_CHIEF],
    ['Admin', SYNTHETIC_ADMIN],
  ])(
    'has no accessibility violations for an %s',
    async (_role, session) => {
      const app = await open(NORTE_ORDER, session);

      // Once the page is shown: axe is slow under load (coverage), and a retry loop would time out.

      expect(await axeViolations(app.container)).toEqual([]);
    },
    20_000,
  );
});
