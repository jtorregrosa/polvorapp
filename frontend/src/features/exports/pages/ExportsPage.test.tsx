import { screen, within } from '@testing-library/react';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import type { ExportCatalogResponse } from '@/api/generated/model';
import { ADMIN_OVERVIEW, EDITION_2031 } from '@/features/comparsa-orders/test-data';
import { problem, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';

const CATALOG: ExportCatalogResponse = {
  definitions: [
    { name: 'powder-supplier', version: 'provisional-1', provisional: true, audience: 'RECIPIENT' },
    { name: 'rental-company', version: 'provisional-1', provisional: true, audience: 'RECIPIENT' },
    { name: 'arms-authority', version: 'provisional-1', provisional: true, audience: 'RECIPIENT' },
    { name: 'comparsa-list', version: 'provisional-1', provisional: true, audience: 'COMPARSA' },
  ],
};

const PATH = `/editions/${EDITION_2031.id}/exports`;
const ORIGINAL_WIDTH = window.innerWidth;

function serve() {
  server.use(
    mock.get(`/api/exports/editions/${EDITION_2031.id}`, () => HttpResponse.json(CATALOG)),
    mock.get('/api/comparsa-orders/overview', () => HttpResponse.json(ADMIN_OVERVIEW)),
  );
}

async function open() {
  serve();
  const app = await renderApp(PATH, { session: SYNTHETIC_ADMIN });
  await screen.findByRole('heading', { level: 1, name: 'Exportaciones de 2031' });
  return app;
}

describe('ExportsPage (spec: Exports screens)', () => {
  afterEach(() => {
    Object.defineProperty(window, 'innerWidth', {
      configurable: true,
      writable: true,
      value: ORIGINAL_WIDTH,
    });
  });

  it('says the formats are provisional', async () => {
    await open();

    expect(screen.getByText('Formatos provisionales')).toBeInTheDocument();
  });

  it('warns which comparsas have no validated order, without blocking the downloads', async () => {
    await open();

    const warning = screen.getByText('Comparsas sin pedido validado').closest('section, [role="note"], div');
    expect(warning).toHaveTextContent('Comparsa Sintética Este (sin preparar)');
    expect(warning).toHaveTextContent('Comparsa Sintética Sur (enviado)');
    expect(warning).not.toHaveTextContent('Comparsa Sintética Norte');
    expect(
      screen.getByRole('button', { name: 'Descargar Intervención de Armas en Excel' }),
    ).not.toHaveAttribute('aria-disabled');
  });

  it('offers each recipient export with what it contains', async () => {
    await open();

    for (const [title, contents] of [
      ['Proveedor de pólvora', 'Sin datos personales'],
      ['Empresa de alquiler', 'Quién alquila un arma o una cantimplora'],
      ['Intervención de Armas', 'Cada arcabucero activo con arma'],
    ] as const) {
      const section = screen.getByRole('region', { name: title });
      expect(section).toHaveTextContent(contents);
      expect(
        within(section).getByRole('button', { name: `Descargar ${title} en Excel` }),
      ).toBeInTheDocument();
      expect(within(section).getByRole('button', { name: `Descargar ${title} en PDF` })).toBeInTheDocument();
    }
  });

  it('offers the list of each comparsa with an order, a draft until it is validated', async () => {
    await open();

    const lists = screen.getByRole('region', { name: 'Listas de las comparsas' });
    expect(
      within(lists).getByRole('button', { name: 'Descargar la lista de Comparsa Sintética Norte en Excel' }),
    ).toBeInTheDocument();
    expect(
      within(lists).getByRole('button', {
        name: 'Descargar la lista de Comparsa Sintética Sur (borrador) en PDF',
      }),
    ).toBeInTheDocument();
    expect(within(lists).queryByText('Comparsa Sintética Este')).not.toBeInTheDocument();
  });

  it('marks the lists of orders not validated with their status', async () => {
    await open();

    const lists = screen.getByRole('region', { name: 'Listas de las comparsas' });
    expect(within(lists).getByText('Enviado')).toBeInTheDocument();
    expect(within(lists).getByText('Validado')).toBeInTheDocument();
  });

  it('warns about nothing when every comparsa has a validated order', async () => {
    server.use(
      mock.get(`/api/exports/editions/${EDITION_2031.id}`, () => HttpResponse.json(CATALOG)),
      mock.get('/api/comparsa-orders/overview', () =>
        HttpResponse.json({
          ...ADMIN_OVERVIEW,
          rows: ADMIN_OVERVIEW.rows.filter((row) => row.status === 'VALIDATED'),
        }),
      ),
    );
    await renderApp(PATH, { session: SYNTHETIC_ADMIN });
    await screen.findByRole('heading', { level: 1, name: 'Exportaciones de 2031' });

    expect(screen.queryByText('Comparsas sin pedido validado')).not.toBeInTheDocument();
  });

  it('says when no comparsa has prepared its order', async () => {
    server.use(
      mock.get(`/api/exports/editions/${EDITION_2031.id}`, () => HttpResponse.json(CATALOG)),
      mock.get('/api/comparsa-orders/overview', () =>
        HttpResponse.json({ ...ADMIN_OVERVIEW, rows: ADMIN_OVERVIEW.rows.filter((row) => !row.orderId) }),
      ),
    );
    await renderApp(PATH, { session: SYNTHETIC_ADMIN });

    expect(await screen.findByText('Ninguna comparsa ha preparado su pedido todavía.')).toBeInTheDocument();
  });

  it('shows the not-found page for an edition that does not exist', async () => {
    server.use(
      mock.get(`/api/exports/editions/${EDITION_2031.id}`, () => problem(404, 'exports.notFound')),
      mock.get('/api/comparsa-orders/overview', () => problem(404, 'editions.notFound')),
    );
    await renderApp(PATH, { session: SYNTHETIC_ADMIN });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Página no encontrada' }),
    ).toBeInTheDocument();
  });

  it('links back to the orders of the edition', async () => {
    await open();

    expect(screen.getByRole('link', { name: 'Pedidos de 2031' })).toHaveAttribute(
      'href',
      `/editions/${EDITION_2031.id}/orders`,
    );
  });

  it('is for Admins only', async () => {
    serve();
    await renderApp(PATH, { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeInTheDocument();
  });

  it('fits a phone', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: 360 });
    await open();

    expect(screen.getByRole('region', { name: 'Intervención de Armas' })).toBeInTheDocument();
  });

  it('has no axe violations', async () => {
    const app = await open();

    expect(await axeViolations(app.container)).toEqual([]);
  }, 20_000);
});
