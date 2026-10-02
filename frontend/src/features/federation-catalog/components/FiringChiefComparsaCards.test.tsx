import { screen, waitFor, within } from '@testing-library/react';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ComparsaResponse } from '@/api/generated/model';
import { renderApp } from '@/test/app';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { LOGO, NORTE, OESTE, SUR } from '../test-data';

const NORTE_WITH_LOGO: ComparsaResponse = { ...NORTE, logo: LOGO };

/** The comparsas API: the server's scope and default "active only" filter, as the real one applies them. */
function comparsas(all: ComparsaResponse[]) {
  const queries: string[] = [];
  server.use(
    mock.get('/api/comparsas', ({ request }) => {
      const url = new URL(request.url);
      queries.push(url.search);
      const includeInactive = url.searchParams.get('includeInactive') === 'true';
      return HttpResponse.json(all.filter((comparsa) => includeInactive || comparsa.active));
    }),
  );
  return queries;
}

describe('FiringChief comparsa cards in the sidebar (platform spec: Application shell)', () => {
  it("shows a FiringChief's active comparsas in the server's order, with the logo or the placeholder", async () => {
    comparsas([NORTE_WITH_LOGO, SUR]);
    await renderApp('/', { session: SYNTHETIC_FIRING_CHIEF });

    const cards = await screen.findByRole('navigation', { name: 'Mis comparsas' });
    const links = within(cards).getAllByRole('link');
    expect(links.map((link) => link.textContent)).toEqual([
      'Comparsa Sintética Norte',
      'Comparsa Sintética Sur',
    ]);
    expect(links[0]).toHaveAttribute('href', `/comparsas/${NORTE.id}`);
    expect(links[0]?.querySelector('img')).toHaveAttribute(
      'src',
      `/api/comparsas/${NORTE.id}/logo?v=${LOGO.version}`,
    );
    expect(links[1]?.querySelector('img')).toBeNull();
  });

  it('leaves out an inactive comparsa', async () => {
    const queries = comparsas([NORTE, OESTE]);
    await renderApp('/', { session: SYNTHETIC_FIRING_CHIEF });

    const cards = await screen.findByRole('navigation', { name: 'Mis comparsas' });

    expect(
      within(cards)
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['Comparsa Sintética Norte']);
    expect(queries).not.toContain('?includeInactive=true');
  });

  it('shows no cards to an Admin and does not ask for them', async () => {
    const queries = comparsas([NORTE, SUR]);
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByRole('navigation', { name: 'Navegación principal' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Mis comparsas' })).not.toBeInTheDocument();
    expect(queries).toEqual([]);
  });

  it('keeps the mark and the navigation when the comparsas cannot be loaded', async () => {
    server.use(mock.get('/api/comparsas', () => new HttpResponse(null, { status: 503 })));
    const app = await renderApp('/', { session: SYNTHETIC_FIRING_CHIEF });

    // Absence only means something once the request has failed.
    await waitFor(() => {
      expect(app.queryClient.getQueryCache().find({ queryKey: ['/api/comparsas'] })?.state.status).toBe(
        'error',
      );
    });
    expect(screen.getByRole('navigation', { name: 'Navegación principal' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'PolvorApp' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Mis comparsas' })).not.toBeInTheDocument();
  });

  it('shows no cards to a FiringChief without active comparsas', async () => {
    comparsas([OESTE]);
    const app = await renderApp('/', { session: SYNTHETIC_FIRING_CHIEF });

    await waitFor(() => {
      expect(app.queryClient.getQueryCache().find({ queryKey: ['/api/comparsas'] })?.state.status).toBe(
        'success',
      );
    });
    expect(screen.getByRole('navigation', { name: 'Navegación principal' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Mis comparsas' })).not.toBeInTheDocument();
  });
});
