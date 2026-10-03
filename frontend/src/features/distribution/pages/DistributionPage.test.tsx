import { screen, within } from '@testing-library/react';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import { renderApp } from '@/test/app';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { NORTE, SUR } from '@/features/federation-catalog/test-data';
import { ADMIN_PLAN, FIRING_CHIEF_PLAN, POWDER_PROXY } from '../test-data';

const PAGE = `/editions/${ADMIN_PLAN.editionId}/distribution`;

function distribution(plan = ADMIN_PLAN) {
  server.use(
    mock.get('/api/comparsas', () => HttpResponse.json([NORTE, SUR])),
    mock.get(`/api/distribution/editions/${plan.editionId}`, () => HttpResponse.json(plan)),
    mock.get(`/api/distribution/editions/${plan.editionId}/proxies`, () => HttpResponse.json([POWDER_PROXY])),
  );
}

/** A 360 px wide phone (NFR-01): the layout itself is checked by the Playwright `mobile-360` project. */
function onPhone() {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
}

describe('DistributionPage (spec: Distribution screens)', () => {
  afterEach(() => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 });
  });

  it('shows the edition, both days and the proxies, and goes back to the edition', async () => {
    distribution();
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    expect(await screen.findByRole('heading', { level: 1, name: 'Reparto de 2031' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Edición 2031/ })).toHaveAttribute(
      'href',
      `/editions/${ADMIN_PLAN.editionId}`,
    );
    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual([
      'Día de reparto de pólvora',
      'Día de reparto de armas',
      'Autorizados de recogida',
    ]);
  });

  it('stacks the slots and the proxies on a phone, keeping every action', async () => {
    onPhone();
    distribution();
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    const proxies = await screen.findByRole('list', { name: 'Lista de autorizados' }, { timeout: 5000 });
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    const collects = await within(proxies).findByText(
      `${POWDER_PROXY.proxy.name} recoge por ${POWDER_PROXY.holder.name}`,
    );
    const proxy = collects.closest('li');
    if (!proxy) throw new Error('No proxy item');
    expect(
      within(proxy).getByRole('button', {
        name: `Imprimir formulario de ${POWDER_PROXY.holder.name} (pólvora)`,
      }),
    ).toBeInTheDocument();
    const slots = screen.getByRole('list', { name: 'Turnos del día de reparto de pólvora' });
    expect(within(slots).getAllByRole('listitem')[0]).toHaveTextContent(`${NORTE.name}Hora: 09:00`);
    expect(
      screen.getByRole('button', { name: 'Editar turnos del día de reparto de pólvora' }),
    ).toBeInTheDocument();
  });

  it("sets the page title to the edition's distribution", async () => {
    distribution(FIRING_CHIEF_PLAN);
    await renderApp(PAGE, { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('heading', { level: 1, name: 'Reparto de 2031' });
    expect(document.title).toContain('Reparto de 2031');
  });
});
