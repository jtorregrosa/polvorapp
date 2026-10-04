import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { ArquebusierRowResponse, ComparsaResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { NORTE, ROW_CUATRO, ROW_DOS, ROW_TRES, ROW_UNO, SUR } from '../test-data';

function registry(
  rows: (query: URLSearchParams) => ArquebusierRowResponse[],
  comparsas: ComparsaResponse[] = [NORTE, SUR],
) {
  server.use(
    mock.get('/api/arquebusiers', ({ request }) =>
      HttpResponse.json(rows(new URL(request.url).searchParams)),
    ),
    mock.get('/api/comparsas', () => HttpResponse.json(comparsas)),
  );
}

const box = (name: string) => screen.getByRole('checkbox', { name: `Seleccionar ${name}` });

beforeEach(() => {
  document.cookie = 'XSRF-TOKEN=token; path=/';
});

afterEach(() => {
  document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 });
});

describe('ArquebusiersPage badges (spec: Badge screens)', () => {
  it('lets an Admin select arquebusiers, see how many and clear them', async () => {
    const user = userEvent.setup();
    registry(() => [ROW_UNO, ROW_DOS, ROW_TRES]);
    await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
    await screen.findAllByRole('checkbox', { name: /^Seleccionar .+, / });

    await user.click(box('García Sintético, Arcabucero'));
    await user.click(box('Ñúñez Sintética, Arcabucera'));

    const bar = screen.getByRole('region', { name: 'Selección' });
    expect(bar).toHaveTextContent('2 seleccionados');
    await user.click(within(bar).getByRole('button', { name: /Imprimir carnets/ }));
    expect(await screen.findByRole('dialog', { name: 'Imprimir carnets' })).toHaveTextContent(
      '2 arcabuceros seleccionados',
    );
    await user.keyboard('{Escape}');

    await user.click(within(bar).getByRole('button', { name: 'Quitar la selección' }));
    expect(screen.queryByRole('region', { name: 'Selección' })).not.toBeInTheDocument();
    expect(box('García Sintético, Arcabucero')).not.toBeChecked();
  });

  it('keeps the selection when the comparsa filter changes and prints it across comparsas', async () => {
    const user = userEvent.setup();
    const recorded = recordBodies(
      () =>
        new HttpResponse(new Uint8Array([1]), {
          headers: {
            'Content-Disposition': 'attachment; filename=polvorapp-badges-selection-2-20310302.pdf',
          },
        }),
    );
    server.use(mock.post('/api/badges/sheet', recorded.resolver));
    registry((query) => {
      const comparsaId = query.get('comparsaId');
      return [ROW_UNO, ROW_DOS, ROW_TRES].filter((row) => !comparsaId || row.comparsaId === comparsaId);
    });
    await renderApp(`/arquebusiers?comparsaId=${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });
    await user.click(box('García Sintético, Arcabucero'));

    await user.selectOptions(screen.getByRole('combobox', { name: 'Comparsa' }), SUR.id);
    await user.click(
      await screen.findByRole('checkbox', { name: 'Seleccionar Ñúñez Sintética, Arcabucera' }),
    );
    const bar = screen.getByRole('region', { name: 'Selección' });
    expect(bar).toHaveTextContent('2 seleccionados');

    await user.click(within(bar).getByRole('button', { name: /Imprimir carnets/ }));
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('1 sin foto de carnet');
    await user.click(within(dialog).getByRole('button', { name: 'Descargar PDF' }));
    await waitFor(() => {
      expect(recorded.bodies).toEqual([{ arquebusierIds: [ROW_UNO.id, ROW_DOS.id], language: 'es-ES' }]);
    });
  });

  it('offers the whole comparsa when the list is filtered by one and nothing is selected', async () => {
    const user = userEvent.setup();
    registry((query) =>
      [ROW_UNO, ROW_DOS, ROW_TRES].filter((row) => row.comparsaId === query.get('comparsaId')),
    );
    await renderApp(`/arquebusiers?comparsaId=${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });

    await user.click(screen.getByRole('button', { name: `Imprimir carnets de ${NORTE.name}` }));

    expect(await screen.findByRole('dialog')).toHaveTextContent(
      `2 arcabuceros de ${NORTE.name}, activos y en reserva`,
    );
  });

  it('refuses more than 200 badges and says why', async () => {
    const user = userEvent.setup();
    const many = Array.from({ length: 201 }, (_, i) => ({
      ...ROW_UNO,
      id: `00000000-0000-4000-8000-${String(i).padStart(12, '0')}`,
      lastName: `Sintético ${String(i).padStart(3, '0')}`,
    }));
    registry(() => many);
    await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
    await screen.findAllByRole('checkbox', { name: /^Seleccionar .+, / });

    await user.selectOptions(screen.getByRole('combobox', { name: 'Filas por página' }), '50');
    for (let page = 0; page < 5; page += 1) {
      await user.click(screen.getByRole('checkbox', { name: 'Seleccionar todos los de esta página' }));
      if (page < 4) await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    }

    const bar = screen.getByRole('region', { name: 'Selección' });
    expect(bar).toHaveTextContent('201 seleccionados');
    expect(bar).toHaveTextContent('Un PDF admite como máximo 200 carnets');
    const trigger = within(bar).getByRole('button', { name: /Imprimir carnets/ });
    expect(trigger).toHaveAttribute('aria-disabled', 'true');
    expect(trigger).toHaveAccessibleDescription(/Un PDF admite como máximo 200 carnets/);
  }, 30_000);

  it('drops from the selection the arquebusiers the registry no longer has', async () => {
    const user = userEvent.setup();
    server.use(
      mock.post('/api/badges/sheet', () =>
        problem(400, 'validation', { errors: { 'arquebusierIds[1]': 'notFound' } }),
      ),
    );
    registry(() => [ROW_UNO, ROW_TRES, ROW_CUATRO]);
    await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
    await screen.findAllByRole('checkbox', { name: /^Seleccionar .+, / });
    await user.click(box('García Sintético, Arcabucero'));
    await user.click(box('Pérez Sintético, Arcabucero'));

    const bar = screen.getByRole('region', { name: 'Selección' });
    await user.click(within(bar).getByRole('button', { name: /Imprimir carnets/ }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Descargar PDF' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'se quitará de la selección al cerrar',
    );
    await user.keyboard('{Escape}');
    expect(await screen.findByRole('region', { name: 'Selección' })).toHaveTextContent('1 seleccionado');
  });

  it('keeps the panel and its explanation when every selected arquebusier is gone', async () => {
    const user = userEvent.setup();
    server.use(
      mock.post('/api/badges/sheet', () =>
        problem(400, 'validation', { errors: { 'arquebusierIds[0]': 'notFound' } }),
      ),
    );
    registry(() => [ROW_UNO, ROW_TRES]);
    await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
    await screen.findAllByRole('checkbox', { name: /^Seleccionar .+, / });
    await user.click(box('García Sintético, Arcabucero'));

    await user.click(
      within(screen.getByRole('region', { name: 'Selección' })).getByRole('button', {
        name: /Imprimir carnets/,
      }),
    );
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Descargar PDF' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'se quitará de la selección al cerrar',
    );
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('region', { name: 'Selección' })).not.toBeInTheDocument();
  });

  it('clears the selection when the user leaves the list', async () => {
    const user = userEvent.setup();
    registry(() => [ROW_UNO, ROW_TRES]);
    const app = await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
    await screen.findAllByRole('checkbox', { name: /^Seleccionar .+, / });
    await user.click(box('García Sintético, Arcabucero'));
    expect(screen.getByRole('region', { name: 'Selección' })).toBeInTheDocument();

    await app.router.navigate('/');
    await waitFor(() => {
      expect(screen.queryByRole('table', { name: 'Arcabuceros' })).not.toBeInTheDocument();
    });
    await app.router.navigate('/arquebusiers');
    await screen.findAllByRole('checkbox', { name: /^Seleccionar .+, / });

    expect(screen.queryByRole('region', { name: 'Selección' })).not.toBeInTheDocument();
    expect(box('García Sintético, Arcabucero')).not.toBeChecked();
  });

  it('shows FiringChiefs no selection and no badge action', async () => {
    registry(
      (query) =>
        [ROW_UNO, ROW_TRES].filter(
          (row) => !query.get('comparsaId') || row.comparsaId === query.get('comparsaId'),
        ),
      [NORTE],
    );
    await renderApp(`/arquebusiers?comparsaId=${NORTE.id}`, { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('link', { name: 'García Sintético, Arcabucero' });

    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Imprimir carnets/ })).not.toBeInTheDocument();
  });

  it('works on a phone with a checkbox per item and no accessibility violations', async () => {
    const user = userEvent.setup();
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
    registry(() => [ROW_UNO, ROW_DOS]);
    const app = await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
    await screen.findAllByRole('checkbox', { name: /^Seleccionar .+, / });

    await user.click(box('García Sintético, Arcabucero'));

    expect(screen.getByRole('region', { name: 'Selección' })).toHaveTextContent('1 seleccionado');
    expect(await axeViolations(app.container)).toEqual([]);
  });
});
