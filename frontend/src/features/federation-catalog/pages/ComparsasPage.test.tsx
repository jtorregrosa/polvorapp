import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ComparsaResponse } from '@/api/generated/model';
import { problem, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { NORTE, OESTE, SUR } from '../test-data';

function comparsas(respond: (query: URLSearchParams) => ComparsaResponse[]) {
  const queries: string[] = [];
  server.use(
    mock.get('/api/comparsas', ({ request }) => {
      const url = new URL(request.url);
      queries.push(url.search);
      return HttpResponse.json(respond(url.searchParams));
    }),
  );
  return queries;
}

/** The table row that contains `text`. */
function rowOf(container: HTMLElement, text: string): HTMLElement {
  const row = within(container).getByText(text).closest('tr');
  if (!row) throw new Error(`No row for ${text}`);
  return row;
}

describe('ComparsasPage (specs: Comparsas, Comparsa visibility)', () => {
  it('lists comparsas for an Admin with translated side and status, and offers a new one', async () => {
    comparsas(() => [NORTE, SUR]);
    await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });

    const table = await screen.findByRole('table', { name: 'Comparsas' });
    await within(table).findByRole('link', { name: 'Comparsa Sintética Norte' });
    const norte = rowOf(table, 'Comparsa Sintética Norte');
    expect(within(norte).getByText('Cristiano')).toBeInTheDocument();
    expect(within(norte).getByText('Activo')).toBeInTheDocument();
    expect(within(table).getByRole('link', { name: 'Comparsa Sintética Norte' })).toHaveAttribute(
      'href',
      `/comparsas/${NORTE.id}`,
    );
    expect(screen.getByRole('link', { name: 'Nueva comparsa' })).toHaveAttribute('href', '/comparsas/new');
  });

  it('filters by side and includes inactive comparsas through the API, keeping the filters in the address', async () => {
    const user = userEvent.setup();
    const queries = comparsas((query) => (query.get('includeInactive') === 'true' ? [OESTE, SUR] : [SUR]));
    const app = await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('link', { name: 'Comparsa Sintética Sur' });

    await user.selectOptions(screen.getByRole('combobox', { name: 'Bando' }), 'MOORISH');
    await user.click(screen.getByRole('checkbox', { name: 'Incluir inactivas' }));

    await screen.findByRole('link', { name: 'Comparsa Sintética Oeste' });
    expect(queries).toContain('?side=MOORISH&includeInactive=true');
    expect(app.location()).toBe('/comparsas?side=MOORISH&includeInactive=true');
    expect(
      within(rowOf(document.body, 'Comparsa Sintética Oeste')).getByText('Inactivo'),
    ).toBeInTheDocument();
  });

  it('says so when no comparsa matches the filters', async () => {
    comparsas(() => []);
    await renderApp('/comparsas?side=CHRISTIAN', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByText('Ninguna comparsa coincide con estos filtros.')).toBeInTheDocument();
  });

  it('shows a FiringChief all of their comparsas, active or not, without actions or filters', async () => {
    const queries = comparsas(() => [SUR, OESTE]);
    await renderApp('/comparsas', { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('link', { name: 'Comparsa Sintética Sur' });
    expect(screen.getByRole('link', { name: 'Comparsa Sintética Oeste' })).toBeInTheDocument();
    expect(screen.getByText('Las comparsas que tienes asignadas.')).toBeInTheDocument();
    expect(queries).toEqual(['?includeInactive=true']);
    expect(screen.queryByRole('link', { name: 'Nueva comparsa' })).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Bando' })).not.toBeInTheDocument();
  });

  it('tells a FiringChief without comparsas that an Admin must assign one', async () => {
    comparsas(() => []);
    await renderApp('/comparsas', { session: SYNTHETIC_FIRING_CHIEF });

    expect(
      await screen.findByRole('heading', { name: 'Todavía no tienes ninguna comparsa asignada' }),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Un administrador de la Federación tiene que asignarte a tu comparsa.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('shows an error, not an empty state, when the list cannot be loaded', async () => {
    server.use(mock.get('/api/comparsas', () => problem(500, 'unexpected')));
    await renderApp('/comparsas', { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByText('Algo ha fallado. Inténtalo de nuevo.')).toBeInTheDocument();
    expect(screen.queryByText('Todavía no tienes ninguna comparsa asignada')).not.toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('shows the list in Valencian', async () => {
    comparsas(() => [NORTE]);
    await renderApp('/comparsas', { session: SYNTHETIC_ADMIN, language: 'ca-ES-valencia' });

    const table = await screen.findByRole('table', { name: 'Comparses' });
    await within(table).findByText('Cristià');
  });

  it('has no accessibility violations', async () => {
    comparsas(() => [NORTE, SUR]);
    const { container } = await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('link', { name: 'Comparsa Sintética Norte' });

    await waitFor(async () => {
      expect(await axeViolations(container)).toEqual([]);
    });
  });
});
