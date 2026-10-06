import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http as mock, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import type { ComparsaResponse, ComplianceStatisticsResponse } from '@/api/generated/model';
import { NORTE, SUR } from '@/features/federation-catalog/test-data';
import { problem, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { STATISTICS, STATISTICS_EMPTY, TRENDS } from '../test-data';

// Specs "Statistics (UC-07)" and "Statistics screen". Synthetic data only.

function statistics(
  respond: (query: URLSearchParams) => Response = () => HttpResponse.json(STATISTICS),
  comparsas: ComparsaResponse[] = [NORTE, SUR],
) {
  const queries: string[] = [];
  server.use(
    mock.get('/api/compliance/statistics', ({ request }) => {
      const url = new URL(request.url);
      queries.push(url.search);
      return respond(url.searchParams);
    }),
    mock.get('/api/comparsas', () => HttpResponse.json(comparsas)),
  );
  return queries;
}

/** Serves the trends and records the query of each request. */
function trends() {
  const queries: string[] = [];
  server.use(
    mock.get('/api/compliance/trends', ({ request }) => {
      queries.push(new URL(request.url).search);
      return HttpResponse.json(TRENDS);
    }),
  );
  return queries;
}

/** The cell texts of a table row, by its row header. */
function rowTexts(table: HTMLElement, header: string): string[] {
  const row = within(table).getByRole('rowheader', { name: header }).closest('tr');
  if (!row) throw new Error(`No row ${header}`);
  return within(row)
    .getAllByRole('cell')
    .map((cell) => cell.textContent.replace(/\s+/g, ' ').trim());
}

describe('StatisticsPage (spec: Statistics screen)', () => {
  afterEach(() => {
    document.documentElement.classList.remove('dark');
  });

  it('shows the totals and the equality report with counts and shares', async () => {
    statistics();
    await renderApp('/statistics', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByRole('heading', { level: 1, name: 'Estadísticas' })).toBeInTheDocument();
    const gender = await screen.findByRole('table', { name: 'Arcabuceros por género' });
    expect(rowTexts(gender, 'Mujeres')).toEqual(['4', '33 %']);
    expect(rowTexts(gender, 'Hombres')).toEqual(['7', '58 %']);
    expect(rowTexts(gender, 'Sin definir')).toEqual(['1', '8 %']);
    const ages = screen.getByRole('table', { name: 'Tramos de edad por género' });
    expect(rowTexts(ages, 'De 25 a 34')).toEqual(['1', '2', '1', '4', '33 %']);
    expect(
      rowTexts(screen.getByRole('table', { name: 'Arcabuceros por estado de la licencia' }), 'Caduca pronto'),
    ).toEqual(['2', '17 %']);
    expect(rowTexts(screen.getByRole('table', { name: 'Armas propias por tipo' }), 'Arcabuz')).toEqual([
      '3',
      '50 %',
    ]);
  });

  it('shows the first-year arquebusiers by gender, with shares', async () => {
    statistics();
    await renderApp('/statistics', { session: SYNTHETIC_ADMIN });

    const table = await screen.findByRole('table', { name: 'Primer año por género' });
    expect(rowTexts(table, 'En su primer año')).toEqual(['1', '2', '0', '3', '25 %']);
    expect(rowTexts(table, 'No es su primer año')).toEqual(['3', '5', '1', '9', '75 %']);
  });

  it('says the first-year figures come once an earlier edition has orders, instead of zeros', async () => {
    statistics(() => HttpResponse.json({ ...STATISTICS, firstYear: null }));
    await renderApp('/statistics', { session: SYNTHETIC_ADMIN });

    expect(
      await screen.findByText(
        'Las cifras de primer año estarán disponibles cuando una edición anterior tenga pedidos.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Primer año por género' })).not.toBeInTheDocument();
  });

  it('keeps the comparsa and status filters in the address and asks the API for them', async () => {
    const user = userEvent.setup();
    const queries = statistics();
    const app = await renderApp('/statistics', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('table', { name: 'Arcabuceros por género' });

    await user.selectOptions(screen.getByRole('combobox', { name: 'Comparsa' }), NORTE.name);
    await user.selectOptions(screen.getByRole('combobox', { name: 'Estado del arcabucero' }), 'En reserva');

    expect(app.location()).toBe(`/statistics?comparsaId=${NORTE.id}&status=RESERVE`);
    await screen.findByRole('table', { name: 'Arcabuceros por género' });
    expect(queries.at(-1)).toBe(`?comparsaId=${NORTE.id}&status=RESERVE`);
  });

  it('opens with the filters of the address, never asking for unfiltered figures first', async () => {
    const queries = statistics();
    server.use(
      mock.get('/api/comparsas', async () => {
        await delay(50);
        return HttpResponse.json([NORTE, SUR]);
      }),
    );
    await renderApp(`/statistics?comparsaId=${SUR.id}&status=ACTIVE`, { session: SYNTHETIC_ADMIN });

    await screen.findByRole('table', { name: 'Arcabuceros por género' });
    expect(screen.getByRole('combobox', { name: 'Comparsa' })).toHaveDisplayValue(SUR.name);
    expect(screen.getByRole('combobox', { name: 'Estado del arcabucero' })).toHaveDisplayValue('En activo');
    expect(queries).toEqual([`?comparsaId=${SUR.id}&status=ACTIVE`]);
  });

  it('ignores an unknown status in the address', async () => {
    const queries = statistics();
    await renderApp('/statistics?status=INACTIVE', { session: SYNTHETIC_ADMIN });

    await screen.findByRole('table', { name: 'Arcabuceros por género' });
    expect(queries).toEqual(['']);
  });

  it('says when the comparsas behind a filter cannot be loaded, and retries', async () => {
    const user = userEvent.setup();
    let fail = true;
    statistics();
    server.use(
      mock.get('/api/comparsas', () =>
        fail ? problem(500, 'server.error') : HttpResponse.json([NORTE, SUR]),
      ),
    );
    await renderApp(`/statistics?comparsaId=${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    const retry = await screen.findByRole('button', { name: 'Reintentar' });
    expect(screen.getByText(/No se puede mostrar el listado filtrado/)).toBeInTheDocument();
    fail = false;
    await user.click(retry);

    expect(await screen.findByRole('table', { name: 'Arcabuceros por género' })).toBeInTheDocument();
  });

  it('announces how many arquebusiers the figures count', async () => {
    statistics();
    await renderApp('/statistics', { session: SYNTHETIC_ADMIN });

    expect(
      await screen.findByText('Mostrando 12 arcabuceros', { selector: '[role=status]' }),
    ).toBeInTheDocument();
  });

  it('formats the figures in the user language', async () => {
    statistics(() =>
      HttpResponse.json({ ...STATISTICS, total: 1234 } satisfies ComplianceStatisticsResponse),
    );
    await renderApp('/statistics', { session: SYNTHETIC_ADMIN, language: 'en' });

    expect(
      await screen.findByText('Showing 1,234 arquebusiers', { selector: '[role=status]' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Arquebusiers by gender' })).toBeInTheDocument();
  });

  it('keeps the gender figures of each comparsa on a phone', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
    try {
      statistics();
      await renderApp('/statistics', { session: SYNTHETIC_ADMIN });

      const list = await screen.findByRole('list', { name: 'Cifras por comparsa' });
      expect(within(list).getAllByRole('listitem')[0]).toHaveTextContent(
        'Mujeres 2, hombres 4, sin definir 1',
      );
    } finally {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 });
    }
  });

  it('lists the figures of each comparsa, each linking to the comparsa', async () => {
    statistics();
    await renderApp('/statistics', { session: SYNTHETIC_ADMIN });

    const table = await screen.findByRole('table', { name: 'Cifras por comparsa' });
    expect(within(table).getByRole('link', { name: NORTE.name })).toHaveAttribute(
      'href',
      `/comparsas/${NORTE.id}`,
    );
    expect(within(table).getAllByRole('link')).toHaveLength(2);
  });

  it('offers no comparsa filter and no comparsa table with a single comparsa', async () => {
    statistics(
      () => HttpResponse.json({ ...STATISTICS, comparsas: [] } satisfies ComplianceStatisticsResponse),
      [NORTE],
    );
    await renderApp('/statistics', { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('table', { name: 'Arcabuceros por género' });
    expect(screen.queryByRole('combobox', { name: 'Comparsa' })).not.toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Cifras por comparsa' })).not.toBeInTheDocument();
  });

  it('says when no arquebusier matches the filters and clears them', async () => {
    const user = userEvent.setup();
    statistics((query) => HttpResponse.json(query.has('status') ? STATISTICS_EMPTY : STATISTICS));
    const app = await renderApp(`/statistics?comparsaId=${NORTE.id}&status=RESERVE`, {
      session: SYNTHETIC_ADMIN,
    });

    expect(
      await screen.findByRole('heading', { name: 'Ningún arcabucero coincide con los filtros.' }),
    ).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Arcabuceros por género' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Quitar los filtros' }));

    expect(app.location()).toBe('/statistics');
    expect(await screen.findByRole('table', { name: 'Arcabuceros por género' })).toBeInTheDocument();
  });

  it('explains to a FiringChief without comparsas that none is assigned yet', async () => {
    statistics(() => HttpResponse.json(STATISTICS_EMPTY), []);
    await renderApp('/statistics', { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByText('Todavía no tienes ninguna comparsa asignada')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('says when the statistics cannot be loaded and loads them again on retry', async () => {
    const user = userEvent.setup();
    let fail = true;
    statistics(() => (fail ? problem(500, 'server.error') : HttpResponse.json(STATISTICS)));
    await renderApp('/statistics', { session: SYNTHETIC_ADMIN });

    const retry = await screen.findByRole('button', { name: 'Reintentar' });
    fail = false;
    await user.click(retry);

    expect(await screen.findByRole('table', { name: 'Arcabuceros por género' })).toBeInTheDocument();
  });

  it('offers no download', async () => {
    statistics();
    await renderApp('/statistics', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('table', { name: 'Arcabuceros por género' });

    expect(screen.queryByRole('button', { name: /descarg|export/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /descarg|export/i })).not.toBeInTheDocument();
  });

  it.each(['light', 'dark'])('has no accessibility violations in the %s theme', async (theme) => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
    statistics();
    const { container } = await renderApp('/statistics', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('table', { name: 'Cifras por comparsa' });

    expect(await axeViolations(container)).toEqual([]);
  });

  it('says the figures are loading until they arrive', async () => {
    statistics(() => HttpResponse.json(STATISTICS));
    server.use(
      mock.get('/api/compliance/statistics', async () => {
        await delay(100);
        return HttpResponse.json(STATISTICS);
      }),
    );
    await renderApp('/statistics', { session: SYNTHETIC_ADMIN });

    // Said in the status region, shown as section placeholders (UI audit).
    expect(await screen.findByText('Cargando las estadísticas…')).toHaveClass('sr-only');
    expect(document.querySelector('[data-slot="loading-sections"]')).toHaveAttribute('aria-hidden', 'true');
    expect(await screen.findByRole('table', { name: 'Arcabuceros por género' })).toBeInTheDocument();
    expect(screen.queryByText('Cargando las estadísticas…')).not.toBeInTheDocument();
    expect(document.querySelector('[data-slot="loading-sections"]')).toBeNull();
  });

  describe('tabs (spec: Statistics screen, Tabs kept in the address)', () => {
    it('opens on "Today" and keeps the chosen tab in the address', async () => {
      const user = userEvent.setup();
      statistics();
      trends();
      const app = await renderApp('/statistics', { session: SYNTHETIC_ADMIN });

      expect(await screen.findByRole('tab', { name: 'Hoy' })).toHaveAttribute('aria-selected', 'true');
      expect(await screen.findByRole('table', { name: 'Arcabuceros por género' })).toBeInTheDocument();

      await user.click(screen.getByRole('tab', { name: 'Tendencias' }));

      expect(app.location()).toBe('/statistics?view=trends');
      expect(
        await screen.findByRole('heading', { level: 2, name: 'Arcabuceros por edición' }),
      ).toBeInTheDocument();
      // The status filter belongs to "Today" only.
      expect(screen.queryByRole('combobox', { name: 'Estado del arcabucero' })).not.toBeInTheDocument();
    });

    it('opens on "Trends" from the address, with its comparsa filter, without asking for today\'s figures', async () => {
      const today = statistics();
      const asked = trends();
      await renderApp(`/statistics?comparsaId=${NORTE.id}&view=trends`, { session: SYNTHETIC_ADMIN });

      expect(await screen.findByRole('tab', { name: 'Tendencias' })).toHaveAttribute('aria-selected', 'true');
      expect(
        await screen.findByRole('heading', { level: 2, name: 'Arcabuceros por edición' }),
      ).toBeInTheDocument();
      expect(asked).toEqual([`?comparsaId=${NORTE.id}`]);
      expect(today).toEqual([]);
    });

    it('shares the comparsa filter across both tabs', async () => {
      const user = userEvent.setup();
      statistics();
      const asked = trends();
      const app = await renderApp('/statistics?view=trends', { session: SYNTHETIC_ADMIN });

      await user.selectOptions(await screen.findByRole('combobox', { name: 'Comparsa' }), SUR.id);
      await screen.findByRole('heading', { level: 2, name: 'Arcabuceros por edición' });
      expect(asked).toContain(`?comparsaId=${SUR.id}`);

      await user.click(screen.getByRole('tab', { name: 'Hoy' }));

      expect(app.location()).toBe(`/statistics?comparsaId=${SUR.id}`);
      expect(screen.getByRole('combobox', { name: 'Comparsa' })).toHaveValue(SUR.id);
    });

    it('has no accessibility violations on the trends tab', async () => {
      statistics();
      trends();
      const { container } = await renderApp('/statistics?view=trends', { session: SYNTHETIC_ADMIN });

      await screen.findByRole('heading', { level: 2, name: 'Arcabuceros por edición' });

      expect(await axeViolations(container)).toEqual([]);
    });
  });
});
