import { screen, within } from '@testing-library/react';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { OverviewResponse } from '@/api/generated/model';
import { renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { NORTE, SUR } from '@/features/federation-catalog/test-data';
import { ADMIN_OVERVIEW, CHIEF_OVERVIEW, NORTE_ORDER, NO_EDITION_OVERVIEW } from '../test-data';

// The orders card of the start page (UI audit, item 15). Synthetic data only.
function overview(response: OverviewResponse) {
  server.use(
    mock.get('/api/comparsa-orders/overview', () => HttpResponse.json(response)),
    mock.get('/api/arquebusiers', () => HttpResponse.json([])),
    mock.get('/api/comparsas', () => HttpResponse.json([NORTE, SUR])),
  );
}

describe('DashboardOrdersCard', () => {
  it('shows a FiringChief each of their orders with its status, warnings and the way to it', async () => {
    overview(CHIEF_OVERVIEW);
    await renderApp('/', { session: SYNTHETIC_FIRING_CHIEF });

    const card = await screen.findByRole('region', { name: 'Tus pedidos · Fiestas 2031' }, { timeout: 5000 });
    expect(
      within(card).getByRole('link', {
        name: `Ir al pedido de ${CHIEF_OVERVIEW.rows[0]?.comparsa.name ?? ''}`,
      }),
    ).toHaveAttribute('href', `/orders/${NORTE_ORDER.id}`);
    expect(card).toHaveTextContent('Borrador');
    expect(card).toHaveTextContent('Sin preparar');
    expect(within(card).getByRole('link', { name: 'Preparar el pedido' })).toHaveAttribute('href', '/orders');
    if (NORTE_ORDER.totals.entriesWithWarnings > 0) {
      expect(card).toHaveTextContent(/arcabuceros? con avisos/);
    }
    expect(await axeViolations(card)).toEqual([]);
  });

  it('shows an Admin how many orders wait for review and the count in each status', async () => {
    overview(ADMIN_OVERVIEW);
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    const card = await screen.findByRole('region', { name: 'Pedidos · Fiestas 2031' });
    expect(card).toHaveTextContent('1 pedido por revisar');
    expect(within(card).getByText('Sin preparar').closest('div')).toHaveTextContent('Sin preparar1');
    expect(within(card).getByText('Enviados').closest('div')).toHaveTextContent('Enviados1');
    expect(within(card).getByRole('link', { name: 'Revisar los pedidos' })).toHaveAttribute(
      'href',
      '/orders',
    );
  });

  it('shows nothing without an edition in progress', async () => {
    overview(NO_EDITION_OVERVIEW);
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    await screen.findByRole('heading', { level: 1, name: 'Inicio' });
    expect(screen.queryByRole('region', { name: /^Pedidos · / })).not.toBeInTheDocument();
  });
});
