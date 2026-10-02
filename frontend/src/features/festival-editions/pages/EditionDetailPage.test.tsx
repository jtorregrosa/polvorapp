import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { EditionResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { CATALOGUE, CLOSED_2030, CURRENT_2031, DRAFT_2032 } from '../test-data';

/** `GET /api/editions/:id` answers with the edition, updated by later calls to `set`. */
function editionDetails(initial: EditionResponse) {
  let current = initial;
  server.use(
    mock.get(`/api/editions/${initial.id}`, () => HttpResponse.json(current)),
    mock.get('/api/weapon-models', () => HttpResponse.json(CATALOGUE)),
  );
  return {
    set: (next: Partial<EditionResponse>) => {
      current = { ...current, ...next };
    },
  };
}

function asAdmin(edition: EditionResponse) {
  return renderApp(`/editions/${edition.id}`, { session: SYNTHETIC_ADMIN });
}

async function expectSaved(text: string): Promise<void> {
  await waitFor(() => {
    expect(screen.getByText(text, { selector: '[role=status]' })).toBeInTheDocument();
  });
}

async function openPanel(user: UserEvent, name: string) {
  await user.click(await screen.findByRole('button', { name: `Editar ${name}` }));
  return screen.findByRole('dialog');
}

describe('EditionDetailPage in read mode (spec: Editions screens)', () => {
  it('shows the header, key facts and the four sections', async () => {
    editionDetails(CURRENT_2031);
    await asAdmin(CURRENT_2031);

    expect(await screen.findByRole('heading', { level: 1, name: 'Fiestas 2031' })).toBeInTheDocument();
    expect(screen.getAllByText('En curso').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Pedidos abiertos').length).toBeGreaterThan(0);
    const facts = screen.getByRole('list', { name: 'Datos clave de la edición' });
    expect(facts).toHaveTextContent('Del 22 de abril de 2031 al 25 de abril de 2031');
    expect(facts).toHaveTextContent('Los pedidos se cierran el 10 de febrero de 2031');
    expect(facts).toHaveTextContent('Los jefes de disparo pueden editar los pedidos');
    for (const section of [
      'Fechas y plazo de pedidos',
      'Precios',
      'Modelos de alquiler',
      'Hitos del calendario',
    ]) {
      expect(screen.getByRole('heading', { level: 2, name: section })).toBeInTheDocument();
    }
    expect(screen.getByText('Ya no se alquila')).toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Hitos del calendario' })).toHaveTextContent(
      'Reparto sintético de pólvora',
    );
  });

  it.each([
    ['es-ES', 'Caja de pistones'],
    ['ca-ES-valencia', 'Caixa de pistons'],
    ['en', 'Box of caps'],
  ])('shows prices as currency in %s', async (language, term) => {
    editionDetails(CURRENT_2031);
    await renderApp(`/editions/${CURRENT_2031.id}`, { session: SYNTHETIC_ADMIN, language });

    const caps = (await screen.findByText(term)).parentElement;
    const expected = new Intl.NumberFormat(language === 'en' ? 'en-GB' : language, {
      style: 'currency',
      currency: 'EUR',
    }).format(3.75);
    // toHaveTextContent collapses the non-breaking space Intl puts before the euro sign.
    expect(caps).toHaveTextContent(expected.replace(/\s/g, ' '));
  });

  it('shows a FiringChief the edition without edit, status or orders actions', async () => {
    editionDetails(CURRENT_2031);
    await renderApp(`/editions/${CURRENT_2031.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('heading', { level: 1, name: 'Fiestas 2031' });
    expect(screen.queryByRole('button', { name: /^Editar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cerrar pedidos' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Más acciones' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Añadir hito' })).not.toBeInTheDocument();
  });

  it('shows the not-found page for a draft hidden from the caller', async () => {
    server.use(mock.get(`/api/editions/${DRAFT_2032.id}`, () => problem(404, 'editions.notFound')));
    await renderApp(`/editions/${DRAFT_2032.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Página no encontrada' }),
    ).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    editionDetails(CURRENT_2031);
    const { container } = await asAdmin(CURRENT_2031);
    await screen.findByRole('heading', { level: 1, name: 'Fiestas 2031' });

    await waitFor(async () => {
      expect(await axeViolations(container)).toEqual([]);
    });
  });
});

describe('EditionDetailPage edit panels (spec: Detail pages in read mode)', () => {
  it('saves the prices with the version and the rest of the edition, typed with a comma', async () => {
    const user = userEvent.setup();
    const details = editionDetails(DRAFT_2032);
    const { bodies, resolver } = recordBodies(() => {
      details.set({ prices: { ...DRAFT_2032.prices, capsBox: 4.5 }, version: 8 });
      return HttpResponse.json({ ...DRAFT_2032, prices: { ...DRAFT_2032.prices, capsBox: 4.5 } });
    });
    server.use(mock.put(`/api/editions/${DRAFT_2032.id}`, resolver));
    await asAdmin(DRAFT_2032);

    const panel = await openPanel(user, 'precios');
    const caps = within(panel).getByRole('textbox', { name: /Caja de pistones/ });
    expect(caps).toHaveValue('3,75');
    await user.clear(caps);
    await user.type(caps, '4,50');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await expectSaved('Cambios guardados');
    expect(bodies).toEqual([
      {
        festivalStartsOn: '2032-04-22',
        festivalEndsOn: '2032-04-25',
        ordersOpenOn: null,
        ordersCloseOn: null,
        prices: { powderPerKg: 48, capsBox: 4.5, weaponRental: null, flaskRental: null },
        version: 7,
      },
    ]);
    await waitFor(() => {
      expect(screen.getByText('Caja de pistones').parentElement).toHaveTextContent('4,50 €');
    });
  });

  it('keeps the panel open and reloads when the edition changed meanwhile', async () => {
    const user = userEvent.setup();
    editionDetails(DRAFT_2032);
    server.use(mock.put(`/api/editions/${DRAFT_2032.id}`, () => problem(409, 'editions.modified')));
    await asAdmin(DRAFT_2032);

    const panel = await openPanel(user, 'precios');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(await within(panel).findByText(/Otra persona ha cambiado la edición/)).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('puts the server field errors on their fields', async () => {
    const user = userEvent.setup();
    editionDetails(DRAFT_2032);
    server.use(
      mock.put(`/api/editions/${DRAFT_2032.id}`, () =>
        problem(400, 'validation', { errors: { ordersCloseOn: 'afterFestival' } }),
      ),
    );
    await asAdmin(DRAFT_2032);

    const panel = await openPanel(user, 'fechas y plazo de pedidos');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(
      await within(panel).findByLabelText(/Cierre de pedidos/, { selector: 'input' }),
    ).toHaveAccessibleDescription(/No puede ser posterior al primer día de fiestas/);
  });

  it('checks the dates and the order window before sending anything (BR-10)', async () => {
    const user = userEvent.setup();
    editionDetails(DRAFT_2032);
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(DRAFT_2032));
    server.use(mock.put(`/api/editions/${DRAFT_2032.id}`, resolver));
    await asAdmin(DRAFT_2032);

    const panel = await openPanel(user, 'fechas y plazo de pedidos');
    const date = (name: RegExp) => within(panel).getByLabelText(name, { selector: 'input' });
    fireEvent.change(date(/Primer día de fiestas/), { target: { value: '2033-04-22' } });
    fireEvent.change(date(/Apertura de pedidos/), { target: { value: '2032-02-10' } });
    fireEvent.change(date(/Cierre de pedidos/), { target: { value: '2032-01-10' } });
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(date(/Primer día de fiestas/)).toHaveAccessibleDescription(
        /Tiene que ser del año de la edición/,
      );
    });
    expect(date(/Cierre de pedidos/)).toHaveAccessibleDescription(
      /No puede ser anterior a la apertura de pedidos/,
    );
    expect(bodies).toEqual([]);
  });

  it('saves the dates and the order window', async () => {
    const user = userEvent.setup();
    editionDetails(DRAFT_2032);
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(DRAFT_2032));
    server.use(mock.put(`/api/editions/${DRAFT_2032.id}`, resolver));
    await asAdmin(DRAFT_2032);

    const panel = await openPanel(user, 'fechas y plazo de pedidos');
    fireEvent.change(within(panel).getByLabelText(/Apertura de pedidos/, { selector: 'input' }), {
      target: { value: '2032-01-10' },
    });
    fireEvent.change(within(panel).getByLabelText(/Cierre de pedidos/, { selector: 'input' }), {
      target: { value: '2032-02-10' },
    });
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await expectSaved('Cambios guardados');
    expect(bodies).toEqual([
      expect.objectContaining({
        festivalStartsOn: '2032-04-22',
        festivalEndsOn: '2032-04-25',
        ordersOpenOn: '2032-01-10',
        ordersCloseOn: '2032-02-10',
        version: 7,
      }),
    ]);
  });

  it('requires every price once the edition has started', async () => {
    const user = userEvent.setup();
    editionDetails({ ...CURRENT_2031, prices: { ...CURRENT_2031.prices } });
    await asAdmin(CURRENT_2031);

    const panel = await openPanel(user, 'precios');
    expect(within(panel).getByText(/la facturación usará los precios nuevos/)).toBeInTheDocument();
    await user.clear(within(panel).getByRole('textbox', { name: /Alquiler de polvorera/ }));
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(
      await within(panel).findByRole('textbox', {
        name: /Alquiler de polvorera/,
        description: /Este campo es obligatorio/,
      }),
    ).toBeInTheDocument();
  });

  it('offers the rentable catalogue models by kind and only lets a retired one be removed', async () => {
    const user = userEvent.setup();
    editionDetails(CURRENT_2031);
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(CURRENT_2031));
    server.use(mock.put(`/api/editions/${CURRENT_2031.id}/weapon-models`, resolver));
    await asAdmin(CURRENT_2031);

    const panel = await openPanel(user, 'modelos de alquiler');
    expect(within(panel).getByRole('group', { name: 'Arcabuz' })).toBeInTheDocument();
    expect(within(panel).getByRole('group', { name: 'Trabuco' })).toBeInTheDocument();
    expect(within(panel).queryByRole('checkbox', { name: 'PISTOLA' })).not.toBeInTheDocument();
    const retired = within(panel).getByRole('checkbox', { name: 'ARCABUZ MORO ZURDO (PEQUEÑO)' });
    expect(retired).toBeChecked();
    await user.click(retired);
    expect(retired).not.toBeChecked();
    expect(retired).toBeDisabled();
    await user.click(within(panel).getByRole('checkbox', { name: 'TRABUCO CRISTIANO DIESTRO' }));
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await expectSaved('Cambios guardados');
    expect(bodies).toEqual([
      { weaponModelIds: ['00000000-0000-4000-8000-000000000601', '00000000-0000-4000-8000-000000000603'] },
    ]);
  });

  it('adds, edits and removes milestones, and the table follows', async () => {
    const user = userEvent.setup();
    const details = editionDetails(CURRENT_2031);
    const created = {
      id: '00000000-0000-4000-8000-000000000703',
      date: '2031-03-15',
      title: 'Hito sintético',
    };
    const added = recordBodies(() => {
      details.set({ milestones: [...CURRENT_2031.milestones, created] });
      return HttpResponse.json(created, { status: 201 });
    });
    const edited = recordBodies(() => {
      details.set({
        milestones: [...CURRENT_2031.milestones, { ...created, title: 'Hito sintético revisado' }],
      });
      return HttpResponse.json({ ...created, title: 'Hito sintético revisado' });
    });
    const removed: string[] = [];
    server.use(
      mock.post(`/api/editions/${CURRENT_2031.id}/milestones`, added.resolver),
      mock.put(`/api/editions/${CURRENT_2031.id}/milestones/${created.id}`, edited.resolver),
      mock.delete(`/api/editions/${CURRENT_2031.id}/milestones/:milestoneId`, ({ params }) => {
        removed.push(String(params.milestoneId));
        details.set({ milestones: CURRENT_2031.milestones.slice(0, 1) });
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await asAdmin(CURRENT_2031);

    await user.click(await screen.findByRole('button', { name: 'Añadir hito' }));
    const panel = await screen.findByRole('dialog', { name: 'Nuevo hito' });
    await user.type(within(panel).getByRole('textbox', { name: 'Título' }), '  Hito sintético ');
    fireEvent.change(within(panel).getByLabelText('Fecha'), { target: { value: '2031-03-15' } });
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));
    await expectSaved('Hito añadido.');
    expect(added.bodies).toEqual([{ date: '2031-03-15', title: 'Hito sintético' }]);
    const table = screen.getByRole('table', { name: 'Hitos del calendario' });
    expect(await within(table).findByText('Hito sintético')).toBeInTheDocument();

    await user.click(within(table).getByRole('button', { name: 'Editar el hito Hito sintético' }));
    const editPanel = await screen.findByRole('dialog', { name: 'Editar hito' });
    const title = within(editPanel).getByRole('textbox', { name: 'Título' });
    await user.clear(title);
    await user.type(title, 'Hito sintético revisado');
    await user.click(within(editPanel).getByRole('button', { name: 'Guardar cambios' }));
    await expectSaved('Cambios guardados');
    expect(edited.bodies).toEqual([{ date: '2031-03-15', title: 'Hito sintético revisado' }]);
    expect(await within(table).findByText('Hito sintético revisado')).toBeInTheDocument();

    await user.click(
      within(table).getByRole('button', { name: 'Quitar el hito Reparto sintético de pólvora' }),
    );
    const confirm = await screen.findByRole('alertdialog', {
      name: '¿Quitar el hito «Reparto sintético de pólvora»?',
    });
    await user.click(within(confirm).getByRole('button', { name: 'Quitar hito' }));
    await expectSaved('Hito quitado.');
    expect(removed).toEqual(['00000000-0000-4000-8000-000000000702']);
    await waitFor(() => {
      expect(within(table).queryByText('Reparto sintético de pólvora')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: 'Añadir hito' })).toHaveFocus();
  });
});

describe('EditionActions (spec: Editions screens, design D10)', () => {
  async function moreAction(user: UserEvent, name: string) {
    await user.click(await screen.findByRole('button', { name: 'Más acciones' }));
    return screen.findByRole('menuitem', { name });
  }

  it('starts a complete draft after confirming, with its version', async () => {
    const user = userEvent.setup();
    const details = editionDetails(DRAFT_2032);
    const { bodies, resolver } = recordBodies(() => {
      details.set({ status: 'IN_PROGRESS', version: 8 });
      return HttpResponse.json({ ...DRAFT_2032, status: 'IN_PROGRESS' });
    });
    server.use(mock.post(`/api/editions/${DRAFT_2032.id}/status`, resolver));
    await asAdmin(DRAFT_2032);

    await user.click(await screen.findByRole('button', { name: 'Iniciar edición' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Iniciar la edición 2032?' });
    expect(dialog).toHaveTextContent('los jefes de disparo la verán');
    await user.click(within(dialog).getByRole('button', { name: 'Iniciar edición' }));

    await expectSaved('Edición 2032 iniciada.');
    expect(bodies).toEqual([{ status: 'IN_PROGRESS', version: 7 }]);
    expect(await screen.findByRole('button', { name: 'Abrir pedidos' })).toBeInTheDocument();
  });

  it('lists the missing fields in words when the draft is incomplete', async () => {
    const user = userEvent.setup();
    editionDetails(DRAFT_2032);
    server.use(
      mock.post(`/api/editions/${DRAFT_2032.id}/status`, () =>
        problem(409, 'editions.incomplete', { missing: ['ordersCloseOn', 'prices.flaskRental'] }),
      ),
    );
    await asAdmin(DRAFT_2032);

    await user.click(await screen.findByRole('button', { name: 'Iniciar edición' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Iniciar edición' }));

    expect(
      await within(dialog).findByText(
        'Para iniciar la edición faltan el cierre de pedidos y el precio del alquiler de polvorera.',
      ),
    ).toBeInTheDocument();
  });

  it('names the edition already in progress', async () => {
    const user = userEvent.setup();
    editionDetails(DRAFT_2032);
    server.use(
      mock.post(`/api/editions/${DRAFT_2032.id}/status`, () =>
        problem(409, 'editions.anotherInProgress', { inProgressYear: 2031 }),
      ),
    );
    await asAdmin(DRAFT_2032);

    await user.click(await screen.findByRole('button', { name: 'Iniciar edición' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Iniciar edición' }));

    expect(await within(dialog).findByText(/La edición 2031 ya está en curso/)).toBeInTheDocument();
  });

  it('offers the deletion of a draft in "More actions"', async () => {
    const user = userEvent.setup();
    editionDetails(DRAFT_2032);
    await asAdmin(DRAFT_2032);

    expect(await moreAction(user, 'Eliminar edición')).toBeInTheDocument();
  });

  it('closes the open orders and keeps the edition moves disabled until then', async () => {
    const user = userEvent.setup();
    const details = editionDetails(CURRENT_2031);
    const { bodies, resolver } = recordBodies(() => {
      details.set({ ordersOpen: false, version: 12 });
      return HttpResponse.json({ ...CURRENT_2031, ordersOpen: false });
    });
    server.use(mock.post(`/api/editions/${CURRENT_2031.id}/orders`, resolver));
    await asAdmin(CURRENT_2031);

    expect(await moreAction(user, 'Cerrar edición (Cierra antes los pedidos.)')).toHaveAttribute(
      'aria-disabled',
      'true',
    );
    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('button', { name: 'Cerrar pedidos' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Cerrar los pedidos de 2031?' });
    expect(dialog).toHaveTextContent('solo lectura para los jefes de disparo');
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar pedidos' }));

    await expectSaved('Pedidos de 2031 cerrados.');
    expect(bodies).toEqual([{ open: false, version: 11 }]);
    expect(await screen.findByRole('button', { name: 'Abrir pedidos' })).toBeInTheDocument();
    expect(await moreAction(user, 'Volver a preparación')).not.toHaveAttribute('aria-disabled');
  });

  it('reopens a closed edition from "More actions"', async () => {
    const user = userEvent.setup();
    editionDetails(CLOSED_2030);
    const { bodies, resolver } = recordBodies(() =>
      HttpResponse.json({ ...CLOSED_2030, status: 'IN_PROGRESS' }),
    );
    server.use(mock.post(`/api/editions/${CLOSED_2030.id}/status`, resolver));
    await asAdmin(CLOSED_2030);

    await user.click(await moreAction(user, 'Reabrir edición'));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Reabrir la edición 2030?' });
    await user.click(within(dialog).getByRole('button', { name: 'Reabrir edición' }));

    await expectSaved('Edición 2030 reabierta.');
    expect(bodies).toEqual([{ status: 'IN_PROGRESS', version: CLOSED_2030.version }]);
  });
});
