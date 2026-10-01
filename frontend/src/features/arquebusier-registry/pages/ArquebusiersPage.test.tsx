import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ArquebusierRowResponse, ComparsaResponse } from '@/api/generated/model';
import { renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { NORTE, OESTE, ROW_DOS, ROW_TRES, ROW_UNO, SUR } from '../test-data';

function registry(
  rows: (query: URLSearchParams) => ArquebusierRowResponse[],
  comparsas: ComparsaResponse[] = [NORTE, SUR],
) {
  const queries: string[] = [];
  server.use(
    mock.get('/api/arquebusiers', ({ request }) => {
      const url = new URL(request.url);
      queries.push(url.search);
      return HttpResponse.json(rows(url.searchParams));
    }),
    mock.get('/api/comparsas', () => HttpResponse.json(comparsas)),
  );
  return queries;
}

/** The table row that contains `text`. */
function rowOf(text: string): HTMLElement {
  const row = screen.getByText(text).closest('tr');
  if (!row) throw new Error(`No row for ${text}`);
  return row;
}

describe('ArquebusiersPage (spec: Arquebusier visibility, Registry screens)', () => {
  it('lists arquebusiers with their identifiers, comparsa, status and license', async () => {
    registry(() => [ROW_UNO, ROW_DOS]);
    await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });

    const table = await screen.findByRole('table', { name: 'Arcabuceros' });
    const link = await within(table).findByRole('link', { name: 'García Sintético, Arcabucero' });
    expect(link).toHaveAttribute('href', `/arquebusiers/${ROW_UNO.id}`);
    const uno = rowOf('00000001R');
    expect(within(uno).getByText('100001')).toBeInTheDocument();
    expect(within(uno).getByText(NORTE.name)).toBeInTheDocument();
    expect(within(uno).getByText('Activo')).toBeInTheDocument();
    expect(within(uno).getByText('Vigente')).toBeInTheDocument();
    const dos = rowOf('X0000002T');
    expect(within(dos).getByText('Reserva')).toBeInTheDocument();
    expect(within(dos).getByText('Sin licencia')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Registrar arcabucero' })).toHaveAttribute(
      'href',
      '/arquebusiers/new',
    );
  });

  it.each([
    ['garcia', 'García Sintético, Arcabucero'],
    ['ÑÚÑEZ', 'Ñúñez Sintética, Arcabucera'],
    ['x0000002', 'Ñúñez Sintética, Arcabucera'],
    ['100003', 'Pérez Sintético, Arcabucero'],
  ])('searches %s ignoring case and accents, without putting it in the address', async (term, found) => {
    const user = userEvent.setup();
    registry(() => [ROW_UNO, ROW_DOS, ROW_TRES]);
    const app = await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
    const table = await screen.findByRole('table', { name: 'Arcabuceros' });
    await within(table).findByRole('link', { name: 'García Sintético, Arcabucero' });

    await user.type(screen.getByRole('searchbox', { name: 'Buscar arcabuceros' }), term);

    expect(
      within(table)
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual([found]);
    expect(app.location()).toBe('/arquebusiers');
  });

  it('says so when the search matches nobody', async () => {
    const user = userEvent.setup();
    registry(() => [ROW_UNO]);
    await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });

    await user.type(screen.getByRole('searchbox', { name: 'Buscar arcabuceros' }), 'zzz');

    expect(screen.getByText('Ningún arcabucero coincide con la búsqueda o los filtros.')).toBeInTheDocument();
  });

  it('filters by comparsa and status through the API, keeping the filters in the address', async () => {
    const user = userEvent.setup();
    const queries = registry((query) => (query.get('status') === 'RESERVE' ? [ROW_DOS] : [ROW_UNO, ROW_DOS]));
    const app = await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });

    await user.selectOptions(screen.getByRole('combobox', { name: 'Comparsa' }), SUR.id);
    await user.selectOptions(screen.getByRole('combobox', { name: 'Estado' }), 'RESERVE');

    await screen.findByRole('link', { name: 'Ñúñez Sintética, Arcabucera' });
    expect(screen.queryByRole('link', { name: 'García Sintético, Arcabucero' })).not.toBeInTheDocument();
    expect(queries).toContain(`?comparsaId=${SUR.id}&status=RESERVE`);
    expect(app.location()).toBe(`/arquebusiers?comparsaId=${SUR.id}&status=RESERVE`);
  });

  it('hides the comparsa filter, not the column, for a FiringChief with one comparsa', async () => {
    registry(() => [ROW_UNO], [NORTE]);
    await renderApp('/arquebusiers', { session: SYNTHETIC_FIRING_CHIEF });

    const table = await screen.findByRole('table', { name: 'Arcabuceros' });
    await within(table).findByRole('link', { name: 'García Sintético, Arcabucero' });
    expect(screen.queryByRole('combobox', { name: 'Comparsa' })).not.toBeInTheDocument();
    expect(within(table).getByRole('columnheader', { name: /Comparsa/ })).toBeInTheDocument();
    expect(screen.getByText('Los arcabuceros de tus comparsas.')).toBeInTheDocument();
  });

  it("does not offer to register in a FiringChief's inactive comparsas only", async () => {
    registry(() => [], [OESTE]);
    await renderApp('/arquebusiers', { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByText('Todavía no hay arcabuceros');
    expect(screen.queryByRole('link', { name: 'Registrar arcabucero' })).not.toBeInTheDocument();
  });

  it('explains that a FiringChief without comparsas must be assigned first', async () => {
    registry(() => [], []);
    await renderApp('/arquebusiers', { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByText('Todavía no tienes ninguna comparsa asignada')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Registrar arcabucero' })).not.toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('announces how many arquebusiers remain after searching', async () => {
    const user = userEvent.setup();
    registry(() => [ROW_UNO, ROW_DOS, ROW_TRES]);
    await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
    const search = await screen.findByRole('searchbox', { name: 'Buscar arcabuceros' });
    await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });

    await user.type(search, 'sintético');
    expect(await screen.findByText('2 arcabuceros')).toHaveAttribute('role', 'status');
    await user.type(search, 'zzz');
    expect(
      await screen.findByText('Ningún arcabucero coincide con la búsqueda o los filtros.', {
        selector: '[role=status]',
      }),
    ).toBeInTheDocument();
  });

  it('waits for the comparsas before listing with a comparsa filter from the address', async () => {
    let release: () => void = () => undefined;
    const comparsasLoaded = new Promise<void>((resolve) => {
      release = resolve;
    });
    const queries = registry((query) =>
      query.get('comparsaId') === SUR.id ? [ROW_TRES] : [ROW_UNO, ROW_DOS, ROW_TRES],
    );
    server.use(
      mock.get('/api/comparsas', async () => {
        await comparsasLoaded;
        return HttpResponse.json([NORTE, SUR]);
      }),
    );
    await renderApp(`/arquebusiers?comparsaId=${SUR.id}`, { session: SYNTHETIC_ADMIN });

    await screen.findByRole('heading', { level: 1, name: 'Arcabuceros' });
    expect(queries).toEqual([]);
    release();

    await screen.findByRole('link', { name: 'Pérez Sintético, Arcabucero' });
    expect(queries).toEqual([`?comparsaId=${SUR.id}`]);
  });

  it('offers to try again when the comparsas behind a comparsa filter cannot be loaded', async () => {
    const user = userEvent.setup();
    const queries = registry(() => [ROW_UNO]);
    let fail = true;
    server.use(
      mock.get('/api/comparsas', () =>
        fail ? new HttpResponse(null, { status: 500 }) : HttpResponse.json([NORTE, SUR]),
      ),
    );
    await renderApp(`/arquebusiers?comparsaId=${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    expect(await screen.findByText(/No se puede mostrar el listado filtrado/)).toBeInTheDocument();
    expect(queries).toEqual([]);
    fail = false;
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));

    await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });
    expect(queries).toEqual([`?comparsaId=${NORTE.id}`]);
  });

  it('has no accessibility violations', async () => {
    registry(() => [ROW_UNO, ROW_DOS]);
    const { container } = await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });

    expect(await axeViolations(container)).toEqual([]);
  });
});
