import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { problem, renderApp } from '@/test/app';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { CLOSED_2030, CURRENT_2031, DRAFT_2032 } from '@/features/festival-editions/test-data';
import { ADMIN_PLAN, FIRING_CHIEF_PLAN } from '../test-data';

function distribution(plan = ADMIN_PLAN) {
  server.use(
    mock.get(`/api/distribution/editions/${plan.editionId}`, () => HttpResponse.json(plan)),
    mock.get(`/api/distribution/editions/${plan.editionId}/proxies`, () => HttpResponse.json([])),
  );
}

describe('Distribution routes and navigation (spec: Distribution screens)', () => {
  it.each([
    ['an Admin', SYNTHETIC_ADMIN],
    ['a FiringChief', SYNTHETIC_FIRING_CHIEF],
  ])('offers %s the "Distribution" entry', async (_, session) => {
    await renderApp('/', { session });

    const navigation = await screen.findByRole('navigation', { name: 'Navegación principal' });
    expect(within(navigation).getByRole('link', { name: 'Reparto' })).toHaveAttribute(
      'href',
      '/distribution',
    );
  });

  it("opens the current edition's distribution", async () => {
    server.use(mock.get('/api/editions/current', () => HttpResponse.json({ edition: CURRENT_2031 })));
    distribution(FIRING_CHIEF_PLAN);
    const app = await renderApp('/distribution', { session: SYNTHETIC_FIRING_CHIEF });

    await waitFor(() => {
      expect(app.location()).toBe(`/editions/${CURRENT_2031.id}/distribution`);
    });
    expect(await screen.findByRole('heading', { level: 1, name: 'Reparto de 2031' })).toBeInTheDocument();
  });

  it('says when there is no edition in progress and links to the editions', async () => {
    const user = userEvent.setup();
    const app = await renderApp('/distribution', { session: SYNTHETIC_ADMIN });

    expect(
      await screen.findByRole('heading', { name: 'No hay ninguna edición en curso' }),
    ).toBeInTheDocument();
    await user.click(within(screen.getByRole('main')).getByRole('link', { name: 'Ediciones' }));

    await waitFor(() => {
      expect(app.location()).toBe('/editions');
    });
  });

  it('says so when the current edition cannot be loaded', async () => {
    server.use(mock.get('/api/editions/current', () => problem(503, 'unavailable')));
    await renderApp('/distribution', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByText(/No se ha podido cargar el reparto\./)).toBeInTheDocument();
  });

  it('links from an edition that is not a draft to its distribution', async () => {
    server.use(mock.get(`/api/editions/${CLOSED_2030.id}`, () => HttpResponse.json(CLOSED_2030)));
    await renderApp(`/editions/${CLOSED_2030.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('heading', { level: 1, name: 'Fiestas 2030' });
    expect(within(screen.getByRole('main')).getByRole('link', { name: 'Reparto' })).toHaveAttribute(
      'href',
      `/editions/${CLOSED_2030.id}/distribution`,
    );
  });

  it('offers no distribution link on a draft edition', async () => {
    server.use(mock.get(`/api/editions/${DRAFT_2032.id}`, () => HttpResponse.json(DRAFT_2032)));
    await renderApp(`/editions/${DRAFT_2032.id}`, { session: SYNTHETIC_ADMIN });

    await screen.findByRole('heading', { level: 1, name: 'Fiestas 2032' });
    const main = screen.getByRole('main');
    expect(within(main).queryByRole('link', { name: /Reparto/ })).not.toBeInTheDocument();
  });

  it('shows the not-found page for a FiringChief on a draft edition', async () => {
    server.use(
      mock.get(`/api/distribution/editions/${DRAFT_2032.id}`, () => problem(404, 'editions.notFound')),
    );
    await renderApp(`/editions/${DRAFT_2032.id}/distribution`, { session: SYNTHETIC_FIRING_CHIEF });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Página no encontrada' }),
    ).toBeInTheDocument();
  });

  it('offers a retry when the distribution cannot be loaded', async () => {
    const user = userEvent.setup();
    let fail = true;
    server.use(
      mock.get(`/api/distribution/editions/${ADMIN_PLAN.editionId}`, () =>
        fail ? problem(503, 'unavailable') : HttpResponse.json(ADMIN_PLAN),
      ),
      mock.get(`/api/distribution/editions/${ADMIN_PLAN.editionId}/proxies`, () => HttpResponse.json([])),
    );
    await renderApp(`/editions/${ADMIN_PLAN.editionId}/distribution`, { session: SYNTHETIC_ADMIN });

    expect(await screen.findByText(/No se ha podido cargar el reparto\./)).toBeInTheDocument();
    fail = false;
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Reparto de 2031' })).toBeInTheDocument();
  });
});
