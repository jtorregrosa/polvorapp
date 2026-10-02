import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http as mock, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import type {
  ArquebusierRowResponse,
  ComparsaResponse,
  ComplianceSummaryResponse,
} from '@/api/generated/model';
import { NORTE, SUR } from '@/features/federation-catalog/test-data';
import { problem, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { EXPIRING_ROWS, SUMMARY, SUMMARY_UP_TO_DATE } from '../test-data';

// Spec "Alerts dashboard (UC-06)": the start page within the user's scope. Synthetic data only.

function insights({
  summary = () => HttpResponse.json(SUMMARY),
  rows = EXPIRING_ROWS,
  comparsas = [NORTE, SUR],
}: {
  summary?: () => Response;
  rows?: ArquebusierRowResponse[];
  comparsas?: ComparsaResponse[];
} = {}) {
  server.use(
    mock.get('/api/compliance/summary', summary),
    mock.get('/api/arquebusiers', () => HttpResponse.json(rows)),
    mock.get('/api/comparsas', () => HttpResponse.json(comparsas)),
  );
}

const section = (name: string) => screen.getByRole('region', { name });

describe('DashboardPage (spec: Alerts dashboard)', () => {
  afterEach(() => {
    document.documentElement.classList.remove('dark');
  });

  it('shows the active, reserve and with-warnings figures, each opening the filtered list', async () => {
    insights();
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    const figures = await screen.findByRole('region', { name: 'Arcabuceros' });
    expect(await within(figures).findByRole('link', { name: /En activo/ })).toHaveAttribute(
      'href',
      '/arquebusiers?status=ACTIVE',
    );
    expect(within(figures).getByRole('link', { name: /En activo/ })).toHaveTextContent('42');
    expect(within(figures).getByRole('link', { name: /En reserva/ })).toHaveAttribute(
      'href',
      '/arquebusiers?status=RESERVE',
    );
    expect(within(figures).getByRole('link', { name: /Con avisos/ })).toHaveAttribute(
      'href',
      '/arquebusiers?warning=ANY',
    );
    expect(within(figures).getByRole('link', { name: /Con avisos/ })).toHaveTextContent('17');
  });

  it('shows a figure for every warning, zeros included, each opening the list filtered by it', async () => {
    insights();
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    const warnings = await screen.findByRole('region', { name: 'Avisos' });
    const links = await within(warnings).findAllByRole('link');
    expect(links.map((link) => link.getAttribute('href'))).toEqual(
      SUMMARY.warnings.map((warning) => `/arquebusiers?warning=${warning.code}`),
    );
    expect(within(warnings).getByRole('link', { name: /Sin fotos de la licencia/ })).toHaveTextContent('0');
    expect(within(warnings).getByRole('link', { name: /Sin curso/ })).toHaveTextContent('4');
  });

  it('says how many arquebusiers need attention', async () => {
    insights();
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    const banner = (await screen.findByText('17 arcabuceros necesitan atención.')).closest('[data-severity]');
    expect(banner).toHaveAttribute('data-severity', 'warning');
  });

  it('says that everyone is up to date when nothing is pending', async () => {
    insights({ summary: () => HttpResponse.json(SUMMARY_UP_TO_DATE satisfies ComplianceSummaryResponse) });
    await renderApp('/', { session: SYNTHETIC_FIRING_CHIEF });

    const banner = (await screen.findByText(/Todos los arcabuceros están al día/)).closest('[data-severity]');
    expect(banner).toHaveAttribute('data-severity', 'success');
    const warnings = section('Avisos');
    expect(
      within(warnings)
        .getAllByRole('link')
        .map((link) => link.textContent.replace(/\D/g, '')),
    ).toEqual(Array.from({ length: 8 }, () => '0'));
  });

  it('lists the 10 licenses that expire first, by date and then name, with a link to all of them', async () => {
    insights();
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    const expiries = await screen.findByRole('region', { name: 'Próximas caducidades' });
    const table = await within(expiries).findByRole('table', {
      name: 'Arcabuceros cuya licencia caduca antes',
    });
    await waitFor(() => {
      expect(within(table).getAllByRole('link')).toHaveLength(10);
    });
    expect(
      within(table)
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(
      Array.from({ length: 10 }, (_, index) => `Sintético ${String(index + 1).padStart(2, '0')}, Arcabucero`),
    );
    expect(within(table).getAllByRole('link')[0]).toHaveAttribute(
      'href',
      '/arquebusiers/00000000-0000-4000-8000-000000000701',
    );
    expect(
      within(expiries).getByRole('link', { name: 'Ver todas las licencias que caducan pronto' }),
    ).toHaveAttribute('href', '/arquebusiers?license=EXPIRING');
  });

  it('says so when no license expires soon', async () => {
    insights({ summary: () => HttpResponse.json(SUMMARY_UP_TO_DATE), rows: [] });
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByText('Ninguna licencia caduca en los próximos 12 meses.')).toBeInTheDocument();
  });

  it('explains to a FiringChief without comparsas that none is assigned yet, without figures', async () => {
    insights({ comparsas: [] });
    await renderApp('/', { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByText('Todavía no tienes ninguna comparsa asignada')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Arcabuceros' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Avisos' })).not.toBeInTheDocument();
  });

  it('shows no figure to a FiringChief while their comparsas are still unknown', async () => {
    server.use(
      mock.get('/api/comparsas', async () => {
        await delay(100);
        return HttpResponse.json([]);
      }),
      mock.get('/api/compliance/summary', () => HttpResponse.json(SUMMARY)),
    );
    await renderApp('/', { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('heading', { level: 1 });
    expect(screen.queryByRole('region', { name: 'Arcabuceros' })).not.toBeInTheDocument();
    expect(await screen.findByText('Todavía no tienes ninguna comparsa asignada')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Arcabuceros' })).not.toBeInTheDocument();
  });

  it('says when the next expiries cannot be loaded and retries', async () => {
    const user = userEvent.setup();
    let fail = true;
    insights();
    server.use(
      mock.get('/api/arquebusiers', () =>
        fail ? problem(500, 'server.error') : HttpResponse.json(EXPIRING_ROWS),
      ),
    );
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    const retry = await screen.findByRole('button', { name: 'Reintentar' });
    fail = false;
    await user.click(retry);

    expect(
      await screen.findByRole('table', { name: 'Arcabuceros cuya licencia caduca antes' }),
    ).toBeInTheDocument();
  });

  it('says when the summary cannot be loaded and loads it again on retry', async () => {
    const user = userEvent.setup();
    let fail = true;
    insights({ summary: () => (fail ? problem(500, 'server.error') : HttpResponse.json(SUMMARY)) });
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    const retry = await screen.findByRole('button', { name: 'Reintentar' });
    expect(screen.getByRole('navigation', { name: 'Navegación principal' })).toBeInTheDocument();
    fail = false;
    await user.click(retry);

    expect(await screen.findByRole('region', { name: 'Arcabuceros' })).toBeInTheDocument();
  });

  it.each(['light', 'dark'])('has no accessibility violations in the %s theme', async (theme) => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
    insights();
    const { container } = await renderApp('/', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('region', { name: 'Avisos' });

    expect(await axeViolations(container)).toEqual([]);
  });
});
