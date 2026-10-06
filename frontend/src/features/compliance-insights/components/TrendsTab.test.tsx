import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { beforeEach, describe, expect, it } from 'vitest';
import type { TrendsResponse } from '@/api/generated/model';
import { NORTE, SUR } from '@/features/federation-catalog/test-data';
import { problem, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { stubChartSize } from '@/test/chart-size';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { TRENDS, TRENDS_ONE_COMPARSA, TRENDS_ONE_EDITION } from '../test-data';

// Spec "Trends screen" (add-statistics-trends, design D5). Synthetic data only.

function serve(trends: TrendsResponse | (() => Response), comparsas = [NORTE, SUR]) {
  server.use(
    mock.get('/api/compliance/trends', () =>
      typeof trends === 'function' ? trends() : HttpResponse.json(trends),
    ),
    mock.get('/api/comparsas', () => HttpResponse.json(comparsas)),
  );
}

async function open(session = SYNTHETIC_ADMIN, language?: string) {
  const app = await renderApp('/statistics?view=trends', { session, language });
  await screen.findByRole(
    'heading',
    { level: 2, name: /Arcabuceros por edición|Cifras de/ },
    { timeout: 5000 },
  );
  return app;
}

/** The section of a chart, by its heading. */
function chart(name: string): HTMLElement {
  const section = screen.getByRole('heading', { level: 2, name }).closest('section');
  if (!section) throw new Error(`No section ${name}`);
  return section;
}

function rowTexts(table: HTMLElement, header: string): string[] {
  const row = within(table).getByRole('rowheader', { name: header }).closest('tr');
  if (!row) throw new Error(`No row ${header}`);
  return within(row)
    .getAllByRole('cell')
    .map((cell) => cell.textContent.replace(/\s/g, ' ').trim());
}

describe('TrendsTab (spec: Trends screen)', () => {
  beforeEach(stubChartSize);

  it('shows the six views in order, each with a summary of the latest edition against the previous one', async () => {
    serve(TRENDS);
    await open();

    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual([
      'Arcabuceros por edición',
      'Mujeres entre los arcabuceros en activo',
      'Arcabuceros en su primer año',
      'Pólvora',
      'Pistones',
      'Arma de los arcabuceros en activo',
      'Alquileres por tipo de arma',
      'Arcabuceros en activo por comparsa',
    ]);
    const normalised = (name: string) => chart(name).textContent.replace(/\s/g, ' ');
    expect(normalised('Arcabuceros por edición')).toContain(
      '2031 (provisional): 30 en activo, 25 % más que en 2030.',
    );
    expect(normalised('Mujeres entre los arcabuceros en activo')).toContain(
      '2031 (provisional): 30 % de mujeres entre los arcabuceros en activo (25 % en 2030).',
    );
    expect(normalised('Mujeres entre los arcabuceros en activo')).toContain(
      'En 2031, 2 arcabuceros en activo ya no están en él y cuentan como género desconocido.',
    );
    expect(normalised('Arcabuceros en su primer año')).toContain(
      '2031 (provisional): 6 en su primer año, 100 % más que en 2030.',
    );
    expect(normalised('Arcabuceros en su primer año')).toContain(
      'La edición más antigua con pedidos no aparece',
    );
    expect(normalised('Pólvora')).toContain('2031 (provisional): 60 kg de pólvora, 25 % más que en 2030.');
    expect(normalised('Alquileres por tipo de arma')).toContain(
      'Armas alquiladas en 2031 (provisional): 2 (2 en 2030). Cantimploras alquiladas: 1.',
    );
  });

  it.each([
    ['en', 'Arquebusiers per edition', '2031 (provisional): 30 active, 25% more than 2030.'],
    ['ca-ES-valencia', 'Arcabussers per edició', '2031 (provisional): 30 en actiu, 25 % més que en 2030.'],
  ])('formats the summaries in %s', async (language, title, summary) => {
    serve(TRENDS);
    await renderApp('/statistics?view=trends', { session: SYNTHETIC_ADMIN, language });

    await screen.findByRole('heading', { level: 2, name: title });
    expect(chart(title).textContent.replace(/\s/g, ' ')).toContain(summary);
  });

  it('offers each chart as a table with the provisional edition marked', async () => {
    const user = userEvent.setup();
    serve(TRENDS);
    await open();

    await user.click(
      within(chart('Arcabuceros por edición')).getByRole('button', { name: /Tabla de datos/ }),
    );
    const arquebusiers = screen.getByRole('table', { name: 'Arcabuceros por edición: datos' });
    expect(rowTexts(arquebusiers, '2029')).toEqual(['20', '4']);
    expect(rowTexts(arquebusiers, '2031 (provisional)')).toEqual(['30', '2']);

    await user.click(
      within(chart('Mujeres entre los arcabuceros en activo')).getByRole('button', {
        name: /Tabla de datos/,
      }),
    );
    const women = screen.getByRole('table', { name: 'Mujeres entre los arcabuceros en activo: datos' });
    expect(rowTexts(women, '2031 (provisional)')).toEqual(['30 %', '2']);
  });

  it('lists the active arquebusiers of each comparsa per edition with the change, sortable', async () => {
    const user = userEvent.setup();
    serve(TRENDS);
    await open();

    const table = screen.getByRole('table', { name: 'Arcabuceros en activo por comparsa y edición' });
    // Sortable headers also say their sort state ("…, sin ordenar").
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((header) => header.textContent.split(',')[0]),
    ).toEqual(['Comparsa', '2029', '2030', '2031 (provisional)', 'Cambio respecto a 2030']);
    // The change is digits for the eye and words for screen readers.
    expect(rowTexts(table, NORTE.name)).toEqual(['12', '14', '18', '+4sube 4']);
    expect(rowTexts(table, SUR.name)).toEqual(['8', '10', '12', '+2sube 2']);

    await user.click(within(table).getByRole('button', { name: /Cambio respecto a 2030/ }));

    expect(
      within(table)
        .getAllByRole('rowheader')
        .map((cell) => cell.textContent),
    ).toEqual([SUR.name, NORTE.name]);
  });

  it('sorts the per-comparsa table both ways and says the order', async () => {
    const user = userEvent.setup();
    serve(TRENDS);
    await open();

    const table = screen.getByRole('table', { name: 'Arcabuceros en activo por comparsa y edición' });
    const header = () => within(table).getAllByRole('columnheader')[3];
    await user.click(within(table).getByRole('button', { name: /2031 \(provisional\)/ }));
    expect(header()).toHaveAttribute('aria-sort', 'ascending');
    expect(
      within(table)
        .getAllByRole('rowheader')
        .map((cell) => cell.textContent),
    ).toEqual([SUR.name, NORTE.name]);

    await user.click(within(table).getByRole('button', { name: /2031 \(provisional\)/ }));

    expect(header()).toHaveAttribute('aria-sort', 'descending');
    expect(
      within(table)
        .getAllByRole('rowheader')
        .map((cell) => cell.textContent),
    ).toEqual([NORTE.name, SUR.name]);
  });

  it('explains that the edition in progress is provisional', async () => {
    serve(TRENDS);
    await open();

    expect(
      screen.getByText(
        'Las cifras de una edición en curso (2031) son provisionales: sus pedidos aún pueden cambiar.',
      ),
    ).toBeInTheDocument();
  });

  it('shows no per-comparsa table to a FiringChief with one comparsa', async () => {
    serve(TRENDS_ONE_COMPARSA, [NORTE]);
    await open(SYNTHETIC_FIRING_CHIEF);

    expect(screen.getByRole('heading', { level: 2, name: 'Arcabuceros por edición' })).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { name: 'Arcabuceros en activo por comparsa' }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Comparsa' })).not.toBeInTheDocument();
  });

  it('says trends need two editions and shows the only one as a table, without charts', async () => {
    serve(TRENDS_ONE_EDITION);
    const { container } = await open();

    const section = chart('Cifras de 2031 (provisional)');
    expect(section).toHaveTextContent(
      'Las tendencias necesitan al menos dos ediciones con pedidos. Estas son las cifras de 2031 (provisional).',
    );
    // One row per figure, so the table reads on a phone.
    const table = within(section).getByRole('table');
    expect(rowTexts(table, 'En activo')).toEqual(['30']);
    expect(rowTexts(table, 'Cantimploras')).toEqual(['1']);
    expect(container.querySelector('svg.recharts-surface')).toBeNull();
  });

  it('says when no edition has orders yet', async () => {
    serve({ rows: TRENDS_ONE_EDITION.rows.slice(0, 1), comparsas: [] });
    await renderApp('/statistics?view=trends', { session: SYNTHETIC_ADMIN });

    expect(
      await screen.findByText(
        'Las tendencias necesitan al menos dos ediciones con pedidos, y todavía no hay pedidos en esta selección.',
      ),
    ).toBeInTheDocument();
  });

  it('says when the trends cannot be loaded and loads them again on retry', async () => {
    const user = userEvent.setup();
    let fail = true;
    serve(() => (fail ? problem(500, 'server.error') : HttpResponse.json(TRENDS)));
    await renderApp('/statistics?view=trends', { session: SYNTHETIC_ADMIN });

    const retry = await screen.findByRole('button', { name: 'Reintentar' });
    fail = false;
    await user.click(retry);

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Arcabuceros por edición' }),
    ).toBeInTheDocument();
  });

  it('keeps the comparsa figures readable on a phone', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
    try {
      serve(TRENDS);
      await open();

      const item = screen.getByRole('link', { name: NORTE.name }).closest('li');
      expect(item).toHaveTextContent('2029: 12, 2030: 14 y 2031 (provisional): 18');
      expect(item).toHaveTextContent('Cambio respecto a 2030: +4sube 4');
    } finally {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 });
    }
  });

  it('says a fall in words on a phone', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
    try {
      const falling: TrendsResponse = {
        ...TRENDS,
        rows: TRENDS.rows.map((row) =>
          row.year === 2031
            ? {
                ...row,
                comparsas: [
                  { comparsaId: NORTE.id, active: 11 },
                  { comparsaId: SUR.id, active: 10 },
                ],
              }
            : row,
        ),
      };
      serve(falling);
      await open();

      const north = screen.getByRole('link', { name: NORTE.name }).closest('li');
      const south = screen.getByRole('link', { name: SUR.name }).closest('li');
      expect(north).toHaveTextContent('Cambio respecto a 2030: -3baja 3');
      expect(south).toHaveTextContent('Cambio respecto a 2030: 0sin cambios');
    } finally {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 });
    }
  });

  it.each(['es-ES', 'ca-ES-valencia', 'en'])('has no accessibility violations in %s', async (language) => {
    serve(TRENDS);
    const { container } = await renderApp('/statistics?view=trends', { session: SYNTHETIC_ADMIN, language });
    await screen.findAllByRole('table');

    expect(await axeViolations(container)).toEqual([]);
  });
});
