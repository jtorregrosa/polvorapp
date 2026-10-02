import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ArquebusierRowResponse, ComparsaResponse } from '@/api/generated/model';
import { renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { DETAIL_UNO, NORTE, OESTE, ROW_CUATRO, ROW_DOS, ROW_TRES, ROW_UNO, SUR } from '../test-data';

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

  it('filters by comparsa through the API and by status with its counter, keeping both in the address', async () => {
    const user = userEvent.setup();
    const queries = registry((query) =>
      query.get('comparsaId') === SUR.id ? [ROW_DOS] : [ROW_UNO, ROW_DOS, ROW_TRES],
    );
    const app = await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });

    await user.selectOptions(screen.getByRole('combobox', { name: 'Comparsa' }), SUR.id);
    await user.click(screen.getByRole('button', { name: /En reserva/ }));

    await screen.findByRole('link', { name: 'Ñúñez Sintética, Arcabucera' });
    expect(screen.queryByRole('link', { name: 'García Sintético, Arcabucero' })).not.toBeInTheDocument();
    expect(queries).toContain(`?comparsaId=${SUR.id}`);
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
    expect(await screen.findByText('2 arcabuceros', { selector: '[role=status]' })).toBeInTheDocument();
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

  it('offers to try again when the list cannot be loaded', async () => {
    const user = userEvent.setup();
    let fail = true;
    registry(() => [ROW_UNO]);
    server.use(
      mock.get('/api/arquebusiers', () =>
        fail ? new HttpResponse(null, { status: 500 }) : HttpResponse.json([ROW_UNO]),
      ),
    );
    await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByText('Algo ha fallado. Inténtalo de nuevo.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    fail = false;
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByRole('link', { name: 'García Sintético, Arcabucero' })).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    registry(() => [ROW_UNO, ROW_DOS]);
    const { container } = await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });

    expect(await axeViolations(container)).toEqual([]);
  });

  describe('counters (spec: Arquebusier visibility, counter as a filter)', () => {
    const counters = () => screen.getByRole('group', { name: 'Resumen de arcabuceros' });
    const counter = (name: RegExp) => within(counters()).getByRole('button', { name });

    it('counts the arquebusiers in scope by status and license state', async () => {
      registry(() => [ROW_UNO, ROW_DOS, ROW_TRES]);
      await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
      await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });

      expect(
        within(counters())
          .getAllByRole('button')
          .map((button) => button.getAttribute('aria-labelledby') && button.textContent),
      ).toHaveLength(6);
      expect(counter(/En activo/)).toHaveAccessibleName('2 En activo');
      expect(counter(/En reserva/)).toHaveAccessibleName('1 En reserva');
      expect(counter(/Licencia caducada/)).toHaveAccessibleName('1 Licencia caducada');
      expect(counter(/Licencia en trámite/)).toHaveAccessibleName('0 Licencia en trámite');
      expect(counter(/Sin licencia/)).toHaveAccessibleName('1 Sin licencia');
    });

    it('filters with a counter, announces the count, and lists everyone again when pressed again', async () => {
      const user = userEvent.setup();
      registry(() => [ROW_UNO, ROW_DOS, ROW_TRES]);
      const app = await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
      const table = await screen.findByRole('table', { name: 'Arcabuceros' });
      await within(table).findByRole('link', { name: 'García Sintético, Arcabucero' });

      await user.click(counter(/Licencia caducada/));

      expect(counter(/Licencia caducada/)).toHaveAttribute('aria-pressed', 'true');
      expect(
        within(table)
          .getAllByRole('link')
          .map((link) => link.textContent),
      ).toEqual(['Pérez Sintético, Arcabucero']);
      expect(await screen.findByText('1 arcabucero', { selector: '[role=status]' })).toBeInTheDocument();
      expect(app.location()).toBe('/arquebusiers?license=EXPIRED');

      await user.click(counter(/Licencia caducada/));

      expect(counter(/Licencia caducada/)).toHaveAttribute('aria-pressed', 'false');
      expect(within(table).getAllByRole('link')).toHaveLength(3);
    });

    it('combines a status and a license counter with the search', async () => {
      const user = userEvent.setup();
      registry(() => [ROW_UNO, ROW_DOS, ROW_TRES]);
      await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
      const table = await screen.findByRole('table', { name: 'Arcabuceros' });
      await within(table).findByRole('link', { name: 'García Sintético, Arcabucero' });

      await user.click(counter(/En activo/));
      await user.click(counter(/Licencia caducada/));
      await user.type(screen.getByRole('searchbox', { name: 'Buscar arcabuceros' }), 'garcia');

      expect(
        screen.getByRole('heading', { name: 'Ningún arcabucero coincide con la búsqueda o los filtros.' }),
      ).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Quitar los filtros' }));

      expect(within(screen.getByRole('table', { name: 'Arcabuceros' })).getAllByRole('link')).toHaveLength(3);
      expect(counter(/En activo/)).toHaveAttribute('aria-pressed', 'false');
      expect(screen.getByRole('searchbox', { name: 'Buscar arcabuceros' })).toHaveValue('');
    });

    it('orders the counters active, reserve, expiring, expired, pending and none', async () => {
      registry(() => [ROW_UNO, ROW_DOS, ROW_TRES, ROW_CUATRO]);
      await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
      await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });

      expect(
        within(counters())
          .getAllByRole('button')
          .map((button) => button.textContent.replace(/^\s*\d+\s*/, '')),
      ).toEqual([
        'En activo',
        'En reserva',
        'Licencia caduca prontoEn menos de 12 meses',
        'Licencia caducada',
        'Licencia en trámite',
        'Sin licencia',
      ]);
      expect(counter(/En activo/)).toHaveAccessibleName('3 En activo');
      expect(counter(/Licencia caduca pronto/)).toHaveAccessibleName('1 Licencia caduca pronto');
      expect(counter(/Licencia caduca pronto/)).toHaveAccessibleDescription('En menos de 12 meses');
    });

    it('filters the licenses expiring soon with their counter and keeps it in the address', async () => {
      const user = userEvent.setup();
      registry(() => [ROW_UNO, ROW_TRES, ROW_CUATRO]);
      const app = await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
      const table = await screen.findByRole('table', { name: 'Arcabuceros' });
      await within(table).findByRole('link', { name: 'García Sintético, Arcabucero' });

      await user.click(counter(/Licencia caduca pronto/));

      expect(
        within(table)
          .getAllByRole('link')
          .map((link) => link.textContent),
      ).toEqual(['Sánchez Sintética, Arcabucera']);
      expect(app.location()).toBe('/arquebusiers?license=EXPIRING');
      expect(await screen.findByText('1 arcabucero', { selector: '[role=status]' })).toBeInTheDocument();
    });

    it('honours an expiring-license filter in the address', async () => {
      registry(() => [ROW_UNO, ROW_CUATRO]);
      await renderApp('/arquebusiers?license=EXPIRING', { session: SYNTHETIC_ADMIN });

      const table = await screen.findByRole('table', { name: 'Arcabuceros' });
      expect(
        await within(table).findByRole('link', { name: 'Sánchez Sintética, Arcabucera' }),
      ).toBeInTheDocument();
      expect(within(table).getAllByRole('link')).toHaveLength(1);
      expect(counter(/Licencia caduca pronto/)).toHaveAttribute('aria-pressed', 'true');
    });

    it("counts only the arquebusiers of a FiringChief's comparsas", async () => {
      registry(() => [ROW_UNO, ROW_TRES], [NORTE]);
      await renderApp('/arquebusiers', { session: SYNTHETIC_FIRING_CHIEF });
      await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });

      expect(counter(/En activo/)).toHaveAccessibleName('2 En activo');
      expect(counter(/En reserva/)).toHaveAccessibleName('0 En reserva');
    });

    it('offers no counters to a FiringChief without comparsas', async () => {
      registry(() => [], []);
      await renderApp('/arquebusiers', { session: SYNTHETIC_FIRING_CHIEF });

      await screen.findByText('Todavía no tienes ninguna comparsa asignada');
      expect(screen.queryByRole('group', { name: 'Resumen de arcabuceros' })).not.toBeInTheDocument();
    });
  });

  describe('warning filter (spec: Arquebusier visibility, filter by warning)', () => {
    const warningFilter = () => screen.getByRole('combobox', { name: 'Aviso' });

    it('filters by one warning and keeps it in the address', async () => {
      const user = userEvent.setup();
      registry(() => [ROW_UNO, ROW_DOS, ROW_CUATRO]);
      const app = await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
      const table = await screen.findByRole('table', { name: 'Arcabuceros' });
      await within(table).findByRole('link', { name: 'García Sintético, Arcabucero' });

      await user.selectOptions(warningFilter(), 'Menor de edad');

      expect(
        within(table)
          .getAllByRole('link')
          .map((link) => link.textContent),
      ).toEqual(['Sánchez Sintética, Arcabucera']);
      expect(app.location()).toBe('/arquebusiers?warning=UNDER_AGE');
      expect(await screen.findByText('1 arcabucero', { selector: '[role=status]' })).toBeInTheDocument();
    });

    it('lists only arquebusiers with any warning from a link', async () => {
      registry(() => [ROW_UNO, ROW_DOS, ROW_CUATRO]);
      await renderApp('/arquebusiers?warning=ANY', { session: SYNTHETIC_ADMIN });

      const table = await screen.findByRole('table', { name: 'Arcabuceros' });
      await within(table).findByRole('link', { name: 'Ñúñez Sintética, Arcabucera' });
      expect(
        within(table)
          .getAllByRole('link')
          .map((link) => link.textContent),
      ).toEqual(['Ñúñez Sintética, Arcabucera', 'Sánchez Sintética, Arcabucera']);
      expect(warningFilter()).toHaveDisplayValue('Cualquier aviso');
    });

    it('combines with a counter, says when nothing matches, and clears both', async () => {
      const user = userEvent.setup();
      registry(() => [ROW_UNO, ROW_DOS, ROW_TRES, ROW_CUATRO]);
      const app = await renderApp('/arquebusiers?warning=UNDER_AGE&license=EXPIRED', {
        session: SYNTHETIC_ADMIN,
      });

      expect(
        await screen.findByRole('heading', {
          name: 'Ningún arcabucero coincide con la búsqueda o los filtros.',
        }),
      ).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Quitar los filtros' }));

      expect(within(screen.getByRole('table', { name: 'Arcabuceros' })).getAllByRole('link')).toHaveLength(4);
      expect(app.location()).toBe('/arquebusiers');
      expect(warningFilter()).toHaveDisplayValue('Con o sin avisos');
    });

    it('applies any warning and a license counter together', async () => {
      registry(() => [ROW_UNO, ROW_DOS, ROW_TRES, ROW_CUATRO]);
      await renderApp('/arquebusiers?warning=ANY&license=EXPIRING', { session: SYNTHETIC_ADMIN });

      const table = await screen.findByRole('table', { name: 'Arcabuceros' });
      expect(
        await within(table).findByRole('link', { name: 'Sánchez Sintética, Arcabucera' }),
      ).toBeInTheDocument();
      expect(within(table).getAllByRole('link')).toHaveLength(1);
    });

    it('removes the warning from the address when no warning is chosen', async () => {
      const user = userEvent.setup();
      registry(() => [ROW_UNO, ROW_CUATRO]);
      const app = await renderApp('/arquebusiers?warning=UNDER_AGE', { session: SYNTHETIC_ADMIN });
      await screen.findByRole('link', { name: 'Sánchez Sintética, Arcabucera' });

      await user.selectOptions(warningFilter(), 'Con o sin avisos');

      expect(app.location()).toBe('/arquebusiers');
      expect(within(screen.getByRole('table', { name: 'Arcabuceros' })).getAllByRole('link')).toHaveLength(2);
    });

    it('ignores an unknown warning in the address', async () => {
      registry(() => [ROW_UNO, ROW_CUATRO]);
      await renderApp('/arquebusiers?warning=SOMETHING', { session: SYNTHETIC_ADMIN });

      const table = await screen.findByRole('table', { name: 'Arcabuceros' });
      await within(table).findByRole('link', { name: 'García Sintético, Arcabucero' });
      expect(within(table).getAllByRole('link')).toHaveLength(2);
    });
  });

  describe('rows', () => {
    it('show an expiring license as expiring soon, with its date', async () => {
      registry(() => [ROW_CUATRO]);
      await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
      await screen.findByRole('link', { name: 'Sánchez Sintética, Arcabucera' });

      const cuatro = rowOf('00000004G');
      expect(within(cuatro).getByText('Caduca pronto')).toBeInTheDocument();
      expect(within(cuatro).getByText('Caduca el 31/12/2026')).toBeInTheDocument();
    });

    it('show how many warnings each arquebusier has and their names', async () => {
      registry(() => [ROW_UNO, ROW_CUATRO]);
      await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
      await screen.findByRole('link', { name: 'Sánchez Sintética, Arcabucera' });

      const cuatro = rowOf('00000004G');
      expect(within(cuatro).getByText('3 avisos')).toBeInTheDocument();
      expect(
        within(cuatro).getByText('Licencia caduca pronto, Sin curso y Menor de edad'),
      ).toBeInTheDocument();
      expect(within(rowOf('00000001R')).getByText('Sin avisos')).toBeInTheDocument();
    });

    it('show the warnings on phones too', async () => {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
      try {
        registry(() => [ROW_CUATRO]);
        await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });

        await screen.findByRole('link', { name: 'Sánchez Sintética, Arcabucera' });
        const [cuatro] = within(screen.getByRole('list', { name: 'Arcabuceros' })).getAllByRole('listitem');
        expect(cuatro).toHaveTextContent('Licencia: Caduca pronto');
        expect(cuatro).toHaveTextContent('Caduca el 31/12/2026');
        expect(cuatro).toHaveTextContent('3 avisos: Licencia caduca pronto, Sin curso y Menor de edad');
      } finally {
        Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 });
      }
    });

    it('show the license expiry, the identifiers in a second line and a missing ID photo in words', async () => {
      registry(() => [ROW_UNO, ROW_DOS]);
      await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
      await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });

      const uno = rowOf('00000001R');
      expect(within(uno).getByText('Caduca el 10/03/2030')).toBeInTheDocument();
      expect(within(rowOf('X0000002T')).getByText('Sin foto de carnet')).toBeInTheDocument();
    });

    it('open the record when any cell is clicked', async () => {
      const user = userEvent.setup();
      registry(() => [ROW_UNO]);
      server.use(mock.get(`/api/arquebusiers/${ROW_UNO.id}`, () => HttpResponse.json(DETAIL_UNO)));
      const app = await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
      await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });

      await user.click(within(rowOf('00000001R')).getByText(NORTE.name));

      expect(app.location()).toBe(`/arquebusiers/${ROW_UNO.id}`);
    });

    it('are stacked items on a phone, without a table', async () => {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
      try {
        registry(() => [ROW_UNO, ROW_DOS]);
        await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });

        await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });
        const list = screen.getByRole('list', { name: 'Arcabuceros' });
        expect(screen.queryByRole('table')).not.toBeInTheDocument();
        const [uno] = within(list).getAllByRole('listitem');
        expect(uno).toHaveTextContent('García Sintético, Arcabucero');
        expect(uno).toHaveTextContent('00000001R');
        expect(uno).toHaveTextContent(NORTE.name);
        expect(uno).toHaveTextContent('Vigente');
      } finally {
        Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 });
      }
    });
  });
});
