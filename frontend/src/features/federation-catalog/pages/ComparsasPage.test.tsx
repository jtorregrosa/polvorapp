import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ComparsaResponse } from '@/api/generated/model';
import { problem, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { sortableColumns } from '@/test/table';
import { LOGO, NORTE, OESTE, SUR } from '../test-data';

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

  it('sorts by name, side and status', async () => {
    const user = userEvent.setup();
    comparsas(() => [NORTE, OESTE]);
    await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });
    const table = await screen.findByRole('table', { name: 'Comparsas' });
    await within(table).findByRole('link', { name: NORTE.name });

    expect(sortableColumns(table)).toEqual(['Nombre', 'Bando', 'Estado']);
    // "Moro" after "Cristiano": twice for descending, the opposite of the server's order.
    await user.click(within(table).getByRole('button', { name: /^Bando/ }));
    await user.click(within(table).getByRole('button', { name: /^Bando/ }));
    const names = within(table)
      .getAllByRole('link')
      .map((link) => link.textContent);
    expect(names).toEqual([OESTE.name, NORTE.name]);
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

    // The sidebar also links their active comparsas: look in the page itself.
    const main = screen.getByRole('main');
    await within(main).findByRole('link', { name: 'Comparsa Sintética Sur' });
    expect(within(main).getByRole('link', { name: 'Comparsa Sintética Oeste' })).toBeInTheDocument();
    expect(screen.getByText('Las comparsas que tienes asignadas.')).toBeInTheDocument();
    expect(queries).toContain('?includeInactive=true');
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

  it('shows each logo before its name, or the placeholder, without repeating the name (spec: Logo display)', async () => {
    comparsas(() => [{ ...NORTE, logo: LOGO }, SUR]);
    await renderApp('/comparsas', { session: SYNTHETIC_FIRING_CHIEF });

    const table = await screen.findByRole('table', { name: 'Comparsas' });
    await within(table).findByRole('link', { name: 'Comparsa Sintética Norte' });
    const norteLogo = rowOf(table, 'Comparsa Sintética Norte').querySelector(
      '[data-slot="comparsa-logo"] img',
    );
    expect(norteLogo).toHaveAttribute('src', `/api/comparsas/${NORTE.id}/logo?v=${LOGO.version}`);
    expect(norteLogo).toHaveAttribute('alt', '');
    const surTile = rowOf(table, 'Comparsa Sintética Sur').querySelector('[data-slot="comparsa-logo"]');
    expect(surTile).toBeInTheDocument();
    expect(surTile?.querySelector('img')).toBeNull();
  });

  it('shows the logo on phones too', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
    try {
      comparsas(() => [{ ...NORTE, logo: LOGO }]);
      await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });

      const list = await screen.findByRole('list', { name: 'Comparsas' });
      const link = await within(list).findByRole('link', { name: NORTE.name });

      expect(link.closest('li')?.querySelector('[data-slot="comparsa-logo"] img')).toHaveAttribute(
        'src',
        `/api/comparsas/${NORTE.id}/logo?v=${LOGO.version}`,
      );
    } finally {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 });
    }
  });

  it('stacks each comparsa on a phone, with its side and status', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
    try {
      comparsas(() => [NORTE, SUR]);
      await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });

      const list = await screen.findByRole('list', { name: 'Comparsas' });
      // Skeleton items first, then the comparsas.
      await within(list).findByRole('link', { name: NORTE.name });
      const norte = within(list)
        .getAllByRole('listitem')
        .find((item) => item.textContent.includes(NORTE.name));
      if (!norte) throw new Error('No item for Norte');
      expect(norte).toHaveTextContent('Cristiano');
      expect(within(norte).getByRole('link', { name: NORTE.name })).toHaveAttribute(
        'href',
        `/comparsas/${NORTE.id}`,
      );
      expect(screen.queryByRole('table')).not.toBeInTheDocument();
    } finally {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 });
    }
  });

  it('opens a comparsa from anywhere on its row and says how many are listed (list template)', async () => {
    const user = userEvent.setup();
    comparsas(() => [NORTE, SUR]);
    server.use(
      mock.get(`/api/comparsas/${NORTE.id}`, () => HttpResponse.json(NORTE)),
      mock.get(`/api/comparsas/${NORTE.id}/firing-chiefs`, () => HttpResponse.json([])),
    );
    const app = await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });
    const table = await screen.findByRole('table', { name: 'Comparsas' });
    await within(table).findByRole('link', { name: 'Comparsa Sintética Norte' });

    expect(screen.getByText('2 comparsas', { selector: '[role=status]' })).toBeInTheDocument();
    await user.click(within(rowOf(table, 'Comparsa Sintética Norte')).getByText('Cristiano'));

    expect(app.location()).toBe(`/comparsas/${NORTE.id}`);
  });
});
