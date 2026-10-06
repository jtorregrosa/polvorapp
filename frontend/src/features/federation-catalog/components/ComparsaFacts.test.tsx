import { screen, within } from '@testing-library/react';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ArquebusierRowResponse, OverviewResponse } from '@/api/generated/model';
import { ROW_UNO } from '@/features/arquebusier-registry/test-data';
import { renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN } from '@/test/identity';
import { server } from '@/test/server';
import { NORTE, SUR } from '../test-data';

// A comparsa's figures on its page and in the list (UI audit, item 17). Synthetic data only.
const ROWS: ArquebusierRowResponse[] = [
  ROW_UNO,
  { ...ROW_UNO, id: '00000000-0000-4000-8000-000000000391', warnings: ['COURSE_MISSING'] },
  { ...ROW_UNO, id: '00000000-0000-4000-8000-000000000392', status: 'RESERVE', warnings: ['COURSE_MISSING'] },
  { ...ROW_UNO, id: '00000000-0000-4000-8000-000000000393', comparsaId: SUR.id, comparsaName: SUR.name },
];
const ORDER_ID = '00000000-0000-4000-8000-000000000791';
const OVERVIEW: OverviewResponse = {
  edition: {
    id: '00000000-0000-4000-8000-000000000990',
    year: 2031,
    status: 'IN_PROGRESS',
    ordersOpen: true,
  },
  rows: [
    {
      comparsa: NORTE,
      orderId: ORDER_ID,
      status: 'SUBMITTED',
      totals: null,
      canPrepare: false,
      billing: null,
    },
    { comparsa: SUR, orderId: null, status: null, totals: null, canPrepare: true, billing: null },
  ],
  statusCounts: null,
  editionTotals: null,
  editionBilling: null,
};

function serve() {
  server.use(
    mock.get('/api/arquebusiers', () => HttpResponse.json(ROWS)),
    mock.get('/api/comparsa-orders/overview', () => HttpResponse.json(OVERVIEW)),
    mock.get('/api/comparsas', () => HttpResponse.json([NORTE, SUR])),
    mock.get(`/api/comparsas/${NORTE.id}`, () => HttpResponse.json(NORTE)),
    mock.get(`/api/comparsas/${NORTE.id}/firing-chiefs`, () => HttpResponse.json([])),
    mock.get('/api/users', () => HttpResponse.json([])),
  );
}

describe('Comparsa figures', () => {
  it('shows the comparsa’s arquebusiers, warnings and order, each leading to its list or page', async () => {
    serve();
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    const facts = await screen.findByRole('list', { name: 'Cifras de la comparsa' });
    expect(within(facts).getByRole('link', { name: '2 En activo' })).toHaveAttribute(
      'href',
      `/arquebusiers?comparsaId=${NORTE.id}&status=ACTIVE`,
    );
    expect(within(facts).getByRole('link', { name: '1 En reserva' })).toBeInTheDocument();
    expect(within(facts).getByRole('link', { name: '1 Activos con avisos' })).toHaveAttribute(
      'href',
      `/arquebusiers?comparsaId=${NORTE.id}&status=ACTIVE&warning=ANY`,
    );
    expect(facts).toHaveTextContent('Pedido de 2031');
    expect(within(facts).getByRole('link', { name: 'Ver el pedido' })).toHaveAttribute(
      'href',
      `/orders/${ORDER_ID}`,
    );
    expect(await axeViolations(facts)).toEqual([]);
  });

  it('lists each comparsa with its active arquebusiers, those with warnings and its order', async () => {
    serve();
    await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });

    const table = await screen.findByRole('table', { name: /comparsas/i });
    // The order column comes once the figures load.
    await within(table).findByRole('columnheader', { name: /^Pedido/ });
    const norte = within(table).getByText(NORTE.name).closest('tr');
    expect(norte).toHaveTextContent(/Activo.*2.*1.*Enviado/);
    expect(within(table).getByText(SUR.name).closest('tr')).toHaveTextContent('Sin preparar');
  });
});
