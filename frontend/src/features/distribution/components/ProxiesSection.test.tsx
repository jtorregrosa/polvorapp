import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { DistributionPlanResponse, ProxyResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { NORTE, SUR } from '@/features/federation-catalog/test-data';
import {
  ADMIN_PLAN,
  BROKEN_PROXY,
  CANDIDATES,
  CLOSED_PLAN,
  FIRING_CHIEF_PLAN,
  POWDER_PROXY,
} from '../test-data';

const pageOf = (plan: DistributionPlanResponse) => `/editions/${plan.editionId}/distribution`;

/** The distribution API: the plan, the proxies (filtered by comparsa) and Norte's candidates. */
function distribution(plan: DistributionPlanResponse, initial: ProxyResponse[]) {
  let proxies = initial;
  const queries: string[] = [];
  server.use(
    mock.get(`/api/distribution/editions/${plan.editionId}`, () => HttpResponse.json(plan)),
    mock.get(`/api/distribution/editions/${plan.editionId}/proxies`, ({ request }) => {
      const comparsaId = new URL(request.url).searchParams.get('comparsaId');
      queries.push(comparsaId ?? '');
      return HttpResponse.json(proxies.filter((proxy) => !comparsaId || proxy.comparsaId === comparsaId));
    }),
    mock.get(`/api/distribution/editions/${plan.editionId}/comparsas/${NORTE.id}/proxy-candidates`, () =>
      HttpResponse.json(CANDIDATES),
    ),
  );
  return {
    queries,
    set: (next: ProxyResponse[]) => {
      proxies = next;
    },
  };
}

function comparsas(list = [NORTE, SUR]) {
  server.use(mock.get('/api/comparsas', () => HttpResponse.json(list)));
}

const section = () => screen.findByRole('region', { name: 'Autorizados de recogida' }, { timeout: 5000 });

async function table(): Promise<HTMLElement> {
  return within(await section()).findByRole('table', { name: 'Lista de autorizados' });
}

/** The table row whose header is `holder`. */
function rowOf(container: HTMLElement, holder: string): HTMLElement {
  const row = within(container).getByRole('rowheader', { name: holder }).closest('tr');
  if (!row) throw new Error(`No row for ${holder}`);
  return row;
}

describe('Pickup proxies on the distribution page (spec: Distribution screens)', () => {
  it("shows an Admin every comparsa's proxies, with problems, and no form for one that no longer holds", async () => {
    comparsas();
    distribution(ADMIN_PLAN, [POWDER_PROXY, BROKEN_PROXY]);
    await renderApp(pageOf(ADMIN_PLAN), { session: SYNTHETIC_ADMIN });

    const proxies = await table();
    const ok = rowOf(proxies, POWDER_PROXY.holder.name);
    expect(ok).toHaveTextContent('Pólvora');
    expect(ok).toHaveTextContent(POWDER_PROXY.proxy.name);
    expect(ok).toHaveTextContent(NORTE.name);
    expect(
      within(ok).getByRole('button', {
        name: `Imprimir formulario de ${POWDER_PROXY.holder.name} (pólvora)`,
      }),
    ).toBeInTheDocument();
    const broken = rowOf(proxies, BROKEN_PROXY.holder.name);
    expect(broken).toHaveTextContent('Licencia no válida ese día');
    expect(within(broken).queryByRole('button', { name: /Imprimir formulario/ })).not.toBeInTheDocument();
    expect(
      within(await section()).getByText(/queda fuera del listado y no tiene formulario/),
    ).toBeInTheDocument();
  });

  it("filters an Admin's proxies by comparsa", async () => {
    const user = userEvent.setup();
    comparsas();
    const api = distribution(ADMIN_PLAN, [POWDER_PROXY, BROKEN_PROXY]);
    await renderApp(pageOf(ADMIN_PLAN), { session: SYNTHETIC_ADMIN });
    const proxies = await table();
    await within(proxies).findByRole('rowheader', { name: BROKEN_PROXY.holder.name });

    await user.selectOptions(within(await section()).getByRole('combobox', { name: 'Comparsa' }), SUR.name);

    await waitFor(() => {
      expect(
        within(proxies).queryByRole('rowheader', { name: POWDER_PROXY.holder.name }),
      ).not.toBeInTheDocument();
    });
    expect(api.queries).toContain(SUR.id);
  });

  it('removes a proxy after a confirmation', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    const api = distribution(FIRING_CHIEF_PLAN, [POWDER_PROXY]);
    const removed: string[] = [];
    server.use(
      mock.delete(`/api/distribution/proxies/:id`, ({ params }) => {
        removed.push(String(params.id));
        api.set([]);
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(pageOf(FIRING_CHIEF_PLAN), { session: SYNTHETIC_FIRING_CHIEF });
    const proxies = await table();

    await user.click(
      within(rowOf(proxies, POWDER_PROXY.holder.name)).getByRole('button', {
        name: `Eliminar el autorizado de pólvora de ${POWDER_PROXY.holder.name}`,
      }),
    );
    const dialog = await screen.findByRole('alertdialog', {
      name: `¿Eliminar el autorizado de pólvora de ${POWDER_PROXY.holder.name}?`,
    });
    expect(dialog).toHaveTextContent(
      `${POWDER_PROXY.proxy.name} dejará de recoger por ${POWDER_PROXY.holder.name}`,
    );
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar autorizado' }));

    expect(await within(await section()).findByText('No hay autorizados de recogida.')).toBeInTheDocument();
    expect(removed).toEqual([POWDER_PROXY.id]);
  });

  it('registers a proxy, offering only eligible people and saying why the others are not', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    const api = distribution(FIRING_CHIEF_PLAN, []);
    const { bodies, resolver } = recordBodies(() => {
      api.set([POWDER_PROXY]);
      return HttpResponse.json(POWDER_PROXY, { status: 201 });
    });
    server.use(mock.post(`/api/distribution/editions/${FIRING_CHIEF_PLAN.editionId}/proxies`, resolver));
    await renderApp(pageOf(FIRING_CHIEF_PLAN), { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(await within(await section()).findByRole('button', { name: 'Añadir autorizado' }));
    const panel = await screen.findByRole('dialog', { name: 'Añadir un autorizado de recogida' });
    expect(panel).toHaveTextContent('El motivo no se guarda: se escribe a mano en el formulario impreso');
    expect(within(panel).queryByRole('combobox', { name: 'Comparsa' })).not.toBeInTheDocument();
    await user.click(within(panel).getByRole('radio', { name: /Pólvora/ }));

    const holder = await within(panel).findByRole('combobox', { name: /Titular/ });
    await waitFor(() => {
      expect(
        within(holder)
          .getAllByRole('option')
          .map((option) => option.textContent),
      ).toEqual(['Abad Sintética, Ana', 'Climent Sintética, Carla']);
    });
    await user.selectOptions(holder, 'Abad Sintética, Ana');
    const proxy = within(panel).getByRole('combobox', { name: /Autorizado/ });
    expect(within(proxy).getByRole('option', { name: 'Zamora Sintético, Bruno' })).toBeEnabled();
    expect(
      within(proxy).getByRole('option', {
        name: 'Climent Sintética, Carla (no puede: tiene su propio autorizado)',
      }),
    ).toBeDisabled();
    expect(
      within(proxy).getByRole('option', {
        name: 'Domènech Sintètic, Dani (no puede: sin licencia de armas en vigor el día de recogida)',
      }),
    ).toBeDisabled();
    expect(within(proxy).queryByRole('option', { name: /Abad/ })).not.toBeInTheDocument();
    expect(proxy).toHaveAccessibleDescription(
      /No pueden ser autorizados: Climent Sintética, Carla \(no puede/,
    );
    await user.selectOptions(proxy, 'Zamora Sintético, Bruno');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(
      await within(await table()).findByRole('rowheader', { name: POWDER_PROXY.holder.name }),
    ).toBeInTheDocument();
    expect(bodies).toEqual([
      { type: 'POWDER', holderEntryId: CANDIDATES[0]?.entryId, proxyEntryId: CANDIDATES[1]?.entryId },
    ]);
  });

  it.each([
    [
      () => problem(409, 'proxies.alreadyAuthorised'),
      /Titular/,
      'El titular ya tiene un autorizado de este tipo.',
    ],
    [
      () => problem(409, 'proxies.proxyAbsent'),
      /Autorizado/,
      'El autorizado tiene a su vez un autorizado de este tipo',
    ],
    [
      () => problem(400, 'validation', { errors: { proxyEntryId: 'licenseInvalid' } }),
      /Autorizado/,
      'No tiene licencia de armas en vigor ese día.',
    ],
    [
      // Erased on a GDPR request after the panel was opened (add-audit-privacy).
      () => problem(400, 'validation', { errors: { proxyEntryId: 'entryErased' } }),
      /Autorizado/,
      'Es una persona borrada.',
    ],
  ])('shows the translated reason of a rejection on its field', async (answer, field, reason) => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    distribution(FIRING_CHIEF_PLAN, []);
    server.use(mock.post(`/api/distribution/editions/${FIRING_CHIEF_PLAN.editionId}/proxies`, answer));
    await renderApp(pageOf(FIRING_CHIEF_PLAN), { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(await within(await section()).findByRole('button', { name: 'Añadir autorizado' }));
    const panel = await screen.findByRole('dialog', { name: 'Añadir un autorizado de recogida' });
    await user.click(within(panel).getByRole('radio', { name: /Pólvora/ }));
    const holder = await within(panel).findByRole('combobox', { name: /Titular/ });
    await waitFor(() => {
      expect(within(holder).getAllByRole('option')).toHaveLength(2);
    });
    await user.selectOptions(holder, 'Abad Sintética, Ana');
    await user.selectOptions(
      within(panel).getByRole('combobox', { name: /Autorizado/ }),
      'Zamora Sintético, Bruno',
    );
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(within(panel).getByRole('combobox', { name: field })).toHaveAccessibleDescription(
        expect.stringContaining(reason) as string,
      );
    });
  });

  it('treats a proxy already removed elsewhere as removed', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    const api = distribution(FIRING_CHIEF_PLAN, [POWDER_PROXY]);
    server.use(
      mock.delete(`/api/distribution/proxies/:id`, () => {
        api.set([]);
        return problem(404, 'proxies.notFound');
      }),
    );
    await renderApp(pageOf(FIRING_CHIEF_PLAN), { session: SYNTHETIC_FIRING_CHIEF });
    const proxies = await table();

    await user.click(
      within(rowOf(proxies, POWDER_PROXY.holder.name)).getByRole('button', {
        name: /^Eliminar el autorizado/,
      }),
    );
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar autorizado' }));

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    expect(await within(await section()).findByText('No hay autorizados de recogida.')).toBeInTheDocument();
  });

  it('moves focus to "Add proxy" once a proxy is removed', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    const api = distribution(FIRING_CHIEF_PLAN, [POWDER_PROXY]);
    server.use(
      mock.delete(`/api/distribution/proxies/:id`, () => {
        api.set([]);
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(pageOf(FIRING_CHIEF_PLAN), { session: SYNTHETIC_FIRING_CHIEF });
    const proxies = await table();
    await within(await section()).findByRole('button', { name: 'Añadir autorizado' });

    await user.click(
      within(rowOf(proxies, POWDER_PROXY.holder.name)).getByRole('button', {
        name: /^Eliminar el autorizado/,
      }),
    );
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Eliminar autorizado' }),
    );

    await waitFor(() => {
      expect(
        within(screen.getByRole('main')).getByRole('button', { name: 'Añadir autorizado' }),
      ).toHaveFocus();
    });
  });

  it('says so when the people of the order cannot be loaded, and retries', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    distribution(FIRING_CHIEF_PLAN, []);
    let fail = true;
    server.use(
      mock.get(
        `/api/distribution/editions/${FIRING_CHIEF_PLAN.editionId}/comparsas/${NORTE.id}/proxy-candidates`,
        () => (fail ? problem(503, 'unavailable') : HttpResponse.json(CANDIDATES)),
      ),
    );
    await renderApp(pageOf(FIRING_CHIEF_PLAN), { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(await within(await section()).findByRole('button', { name: 'Añadir autorizado' }));
    const panel = await screen.findByRole('dialog', { name: 'Añadir un autorizado de recogida' });
    await user.click(within(panel).getByRole('radio', { name: /Pólvora/ }));
    expect(
      await within(panel).findByText(/No se han podido cargar las inscripciones del pedido/),
    ).toBeInTheDocument();
    fail = false;
    await user.click(within(panel).getByRole('button', { name: 'Reintentar' }));

    expect(await within(panel).findByRole('combobox', { name: /Titular/ })).toBeInTheDocument();
  });

  it('shows a FiringChief a closed edition read-only, with the reason, still printing forms', async () => {
    comparsas([NORTE]);
    distribution(CLOSED_PLAN, [{ ...POWDER_PROXY, editionId: CLOSED_PLAN.editionId }]);
    await renderApp(pageOf(CLOSED_PLAN), { session: SYNTHETIC_FIRING_CHIEF });

    const proxies = await section();
    expect(within(proxies).getByText(/La edición no está en curso/)).toBeInTheDocument();
    expect(within(proxies).queryByRole('button', { name: 'Añadir autorizado' })).not.toBeInTheDocument();
    const row = rowOf(await table(), POWDER_PROXY.holder.name);
    expect(within(row).queryByRole('button', { name: /Eliminar/ })).not.toBeInTheDocument();
    expect(within(row).getByRole('button', { name: /Imprimir formulario/ })).toBeInTheDocument();
  });

  it('has no automatically detectable accessibility violations', async () => {
    comparsas();
    distribution(ADMIN_PLAN, [POWDER_PROXY, BROKEN_PROXY]);
    const { container } = await renderApp(pageOf(ADMIN_PLAN), { session: SYNTHETIC_ADMIN });
    await within(await table()).findByRole('rowheader', { name: POWDER_PROXY.holder.name });

    expect(await axeViolations(container)).toEqual([]);
  });
});
