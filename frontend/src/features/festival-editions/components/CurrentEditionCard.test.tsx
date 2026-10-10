import { screen, waitFor, within } from '@testing-library/react';
import { http as mock, HttpResponse } from 'msw';
import { beforeEach, describe, expect, it } from 'vitest';
import { problem, renderApp } from '@/test/app';
import { AXE_WAIT, axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { CURRENT_2031 } from '../test-data';

/** The alerts dashboard's message when nothing needs attention (insights:dashboard.upToDate). */
const UP_TO_DATE = /al día/;

function currentIs(edition: typeof CURRENT_2031 | null) {
  server.use(mock.get('/api/editions/current', () => HttpResponse.json({ edition })));
}

describe('CurrentEditionCard on the start page (spec: Current edition on the start page)', () => {
  beforeEach(() => {
    // The rest of the start page: no arquebusiers, and the FiringChief's one comparsa.
    server.use(
      mock.get('/api/arquebusiers', () => HttpResponse.json([])),
      mock.get('/api/comparsas', () =>
        HttpResponse.json([
          {
            id: '00000000-0000-4000-8000-000000000901',
            name: 'Comparsa Sintética',
            side: 'MOORISH',
            active: true,
            logo: null,
          },
        ]),
      ),
    );
  });

  it('shows the edition in progress, its orders, the next deadline and the upcoming milestones', async () => {
    const upcoming = [1, 2, 3, 4].map((n) => ({
      id: `00000000-0000-4000-8000-00000000080${String(n)}`,
      date: `2099-0${String(n)}-01`,
      title: `Hito sintético ${String(n)}`,
      notify: false,
    }));
    const past = {
      id: '00000000-0000-4000-8000-000000000800',
      date: '2020-01-01',
      title: 'Plazo sintético pasado',
      notify: false,
    };
    currentIs({ ...CURRENT_2031, milestones: [past, ...upcoming] });
    await renderApp('/', { session: SYNTHETIC_FIRING_CHIEF });

    const heading = await screen.findByRole('heading', { level: 2, name: 'Edición actual' });
    const card = heading.closest('section');
    if (!card) throw new Error('The card is not a section.');
    expect(within(card).getByRole('link', { name: 'Fiestas 2031' })).toHaveAttribute(
      'href',
      `/editions/${CURRENT_2031.id}`,
    );
    expect(card).toHaveTextContent('Pedidos abiertos');
    expect(card).toHaveTextContent('Los jefes de disparo pueden editar los pedidos');
    expect(card).toHaveTextContent('Los pedidos se cierran el 10 de febrero de 2031');
    // Only milestones from today on, three at most.
    expect(card).not.toHaveTextContent('Plazo sintético pasado');
    expect(card).toHaveTextContent('Hito sintético 3');
    expect(card).not.toHaveTextContent('Hito sintético 4');
  });

  it('says no edition is in progress, and links Admins to the editions', async () => {
    currentIs(null);
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByText('No hay ninguna edición en curso')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ir a las ediciones' })).toHaveAttribute('href', '/editions');
  });

  it('offers a FiringChief no link to the editions when none is in progress', async () => {
    currentIs(null);
    await renderApp('/', { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByText('No hay ninguna edición en curso');
    expect(screen.queryByRole('link', { name: 'Ir a las ediciones' })).not.toBeInTheDocument();
  });

  it('keeps the alerts dashboard when the card fails', async () => {
    server.use(mock.get('/api/editions/current', () => problem(500, 'unexpected')));
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByText(/No se ha podido cargar la edición actual/)).toBeInTheDocument();
    expect(await screen.findByText(UP_TO_DATE)).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    currentIs(CURRENT_2031);
    const { container } = await renderApp('/', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('link', { name: 'Fiestas 2031' });

    await waitFor(async () => {
      expect(await axeViolations(container)).toEqual([]);
    }, AXE_WAIT);
  });
});
