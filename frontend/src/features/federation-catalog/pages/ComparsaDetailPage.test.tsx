import { screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ComparsaResponse, FiringChiefResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { asFiringChief, CHIEF_DOS, CHIEF_UNO, NORTE, OESTE } from '../test-data';

/** `GET /api/comparsas/:id` answers with the comparsa, updated by later calls to `set`. */
function comparsaDetails(initial: ComparsaResponse, chiefs: FiringChiefResponse[] = []) {
  let current = initial;
  server.use(
    mock.get(`/api/comparsas/${initial.id}`, () => HttpResponse.json(current)),
    mock.get(`/api/comparsas/${initial.id}/firing-chiefs`, () => HttpResponse.json(chiefs)),
    mock.get('/api/comparsas', () => HttpResponse.json([current])),
    mock.get('/api/users', () => HttpResponse.json([])),
  );
  return {
    set: (next: Partial<ComparsaResponse>) => {
      current = { ...current, ...next };
    },
  };
}

/** The outcome notice is shown and, once mounted, focused (WCAG 2.4.3). */
async function expectFocusedNotice(text: string): Promise<void> {
  const notice = (await screen.findByText(text)).closest('[data-severity]');
  await waitFor(() => {
    expect(notice).toHaveFocus();
  });
}

/** A saved change is announced politely, without moving focus (spec: Detail pages in read mode). */
async function expectSaved(text: string): Promise<void> {
  await waitFor(() => {
    expect(screen.getByText(text, { selector: '[role=status]' })).toBeInTheDocument();
  });
}

async function moreAction(user: UserEvent, name: string): Promise<void> {
  await user.click(await screen.findByRole('button', { name: 'Más acciones' }));
  await user.click(await screen.findByRole('menuitem', { name }));
}

async function editData(user: UserEvent) {
  await user.click(await screen.findByRole('button', { name: 'Editar datos de la comparsa' }));
  return screen.findByRole('dialog', { name: 'Editar datos de la comparsa' });
}

describe('ComparsaFormPage (spec: Comparsa management by Admins)', () => {
  it('creates a comparsa and continues on its page with a notice', async () => {
    const user = userEvent.setup();
    const created: ComparsaResponse = { ...NORTE, name: 'Comparsa Sintética Nueva' };
    comparsaDetails(created);
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(created, { status: 201 }));
    server.use(mock.post('/api/comparsas', resolver));
    const app = await renderApp('/comparsas/new', { session: SYNTHETIC_ADMIN });

    await user.type(await screen.findByRole('textbox', { name: /Nombre/ }), 'Comparsa Sintética Nueva');
    await user.click(screen.getByRole('radio', { name: 'Cristiano' }));
    await user.click(screen.getByRole('button', { name: 'Crear comparsa' }));

    await expectFocusedNotice('Comparsa creada.');
    expect(bodies).toEqual([{ name: 'Comparsa Sintética Nueva', side: 'CHRISTIAN' }]);
    expect(app.location()).toBe(`/comparsas/${created.id}`);
  });

  it('validates the fields before calling the API', async () => {
    const user = userEvent.setup();
    let called = false;
    server.use(
      mock.post('/api/comparsas', () => {
        called = true;
        return HttpResponse.json(NORTE, { status: 201 });
      }),
    );
    await renderApp('/comparsas/new', { session: SYNTHETIC_ADMIN });

    await user.click(await screen.findByRole('button', { name: 'Crear comparsa' }));

    expect(await screen.findByText('Este campo es obligatorio.')).toBeInTheDocument();
    expect(
      screen.getByText('Elige una opción.', { selector: '[data-slot="form-message"]' }),
    ).toBeInTheDocument();
    expect(called).toBe(false);
  });

  it('explains a duplicate name', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/comparsas', () => problem(409, 'comparsas.nameTaken')));
    await renderApp('/comparsas/new', { session: SYNTHETIC_ADMIN });

    await user.type(await screen.findByRole('textbox', { name: /Nombre/ }), 'comparsa sintética norte');
    await user.click(screen.getByRole('radio', { name: 'Cristiano' }));
    await user.click(screen.getByRole('button', { name: 'Crear comparsa' }));

    expect(
      await screen.findByText(/Ya existe una comparsa con ese nombre\./, {
        selector: '[data-slot="form-message"]',
      }),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('group', { name: 'Hay un problema' })).toHaveFocus();
    });
  });

  it('lists a refusal about no field in the error summary and offers to cancel (form template)', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/comparsas', () => problem(500, 'unexpected')));
    await renderApp('/comparsas/new', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByRole('link', { name: 'Cancelar' })).toHaveAttribute('href', '/comparsas');
    await user.type(screen.getByRole('textbox', { name: /Nombre/ }), 'Comparsa Sintética Nueva');
    await user.click(screen.getByRole('radio', { name: 'Moro' }));
    await user.click(screen.getByRole('button', { name: 'Crear comparsa' }));

    const summary = await screen.findByRole('group', { name: 'Hay un problema' });
    expect(summary).toHaveTextContent('Algo ha fallado. Inténtalo de nuevo.');
  });

  it('is Admin-only: a FiringChief gets the not-allowed page and nothing is sent', async () => {
    let posted = false;
    server.use(
      mock.post('/api/comparsas', () => {
        posted = true;
        return HttpResponse.json(NORTE, { status: 201 });
      }),
    );
    await renderApp('/comparsas/new', { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Crear comparsa' })).not.toBeInTheDocument();
    expect(posted).toBe(false);
  });
});

describe('ComparsaDetailPage (specs: Comparsa management by Admins, Deleting comparsas, Detail pages in read mode)', () => {
  it('shows the comparsa read-only with its side and status in the header', async () => {
    comparsaDetails(NORTE, [asFiringChief(CHIEF_UNO)]);
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    const heading = await screen.findByRole('heading', { level: 1, name: NORTE.name });
    const header = heading.closest('header');
    expect(header).toHaveTextContent('Cristiano');
    expect(header).toHaveTextContent('Activo');
    const data = screen.getByRole('region', { name: 'Datos de la comparsa' });
    expect(data).toHaveTextContent('Cristiano');
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'Jefes de disparo' })).toHaveTextContent(CHIEF_UNO.name);
  });

  it('lets an Admin edit the name and side in a panel, announces it and returns focus to "Edit"', async () => {
    const user = userEvent.setup();
    const details = comparsaDetails(NORTE);
    const { bodies, resolver } = recordBodies(() => {
      details.set({ name: 'Comparsa Sintética Nord', side: 'MOORISH' });
      return HttpResponse.json({ ...NORTE, name: 'Comparsa Sintética Nord', side: 'MOORISH' });
    });
    server.use(mock.put(`/api/comparsas/${NORTE.id}`, resolver));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    const panel = await editData(user);
    const name = within(panel).getByRole('textbox', { name: 'Nombre' });
    expect(name).toHaveValue('Comparsa Sintética Norte');
    await user.clear(name);
    await user.type(name, 'Comparsa Sintética Nord');
    await user.click(within(panel).getByRole('radio', { name: 'Moro' }));
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await expectSaved('Cambios guardados');
    expect(bodies).toEqual([{ name: 'Comparsa Sintética Nord', side: 'MOORISH' }]);
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Comparsa Sintética Nord' }),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Editar datos de la comparsa' })).toHaveFocus();
    });
  });

  it('shows a duplicate name on the name field and keeps the panel open', async () => {
    const user = userEvent.setup();
    comparsaDetails(NORTE);
    server.use(mock.put(`/api/comparsas/${NORTE.id}`, () => problem(409, 'comparsas.nameTaken')));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    const panel = await editData(user);
    const name = within(panel).getByRole('textbox', { name: 'Nombre' });
    await user.clear(name);
    await user.type(name, 'Comparsa Sintética Sur');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(
      await within(panel).findByText(/Ya existe una comparsa con ese nombre\./, {
        selector: '[data-slot="form-message"]',
      }),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(within(panel).getByRole('group', { name: 'Hay un problema' })).toHaveFocus();
    });
    // Behind the open panel, the page still shows the stored name.
    expect(screen.getByRole('heading', { level: 1, name: NORTE.name, hidden: true })).toBeInTheDocument();
  });

  it('deactivates from "More actions" after confirmation, and reactivates', async () => {
    const user = userEvent.setup();
    const details = comparsaDetails(NORTE);
    server.use(
      mock.post(`/api/comparsas/${NORTE.id}/deactivate`, () => {
        details.set({ active: false });
        return HttpResponse.json({ ...NORTE, active: false });
      }),
      mock.post(`/api/comparsas/${NORTE.id}/reactivate`, () => {
        details.set({ active: true });
        return HttpResponse.json(NORTE);
      }),
    );
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    await moreAction(user, 'Desactivar comparsa');
    const dialog = await screen.findByRole('alertdialog', { name: '¿Desactivar Comparsa Sintética Norte?' });
    await user.click(within(dialog).getByRole('button', { name: 'Desactivar comparsa' }));

    await expectSaved('Comparsa desactivada.');
    expect(await screen.findByText(/Esta comparsa está inactiva/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Más acciones' })).toHaveFocus();
    await moreAction(user, 'Reactivar comparsa');
    await expectSaved('Comparsa reactivada.');
    await waitFor(() => {
      expect(screen.queryByText(/Esta comparsa está inactiva/)).not.toBeInTheDocument();
    });
  });

  it('sets the destructive action apart in "More actions"', async () => {
    const user = userEvent.setup();
    comparsaDetails(NORTE);
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    await user.click(await screen.findByRole('button', { name: 'Más acciones' }));
    const menu = await screen.findByRole('menu');
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent),
    ).toEqual(['Desactivar comparsa', 'Eliminar comparsa']);
    expect(within(menu).getByRole('separator')).toBeInTheDocument();
  });

  it('deletes after a confirmation that counts the FiringChiefs losing access, and returns to the list', async () => {
    const user = userEvent.setup();
    comparsaDetails(NORTE, [asFiringChief(CHIEF_UNO), asFiringChief(CHIEF_DOS)]);
    let deleted = false;
    server.use(
      mock.delete(`/api/comparsas/${NORTE.id}`, () => {
        deleted = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const app = await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    await screen.findByText(CHIEF_DOS.name);

    await moreAction(user, 'Eliminar comparsa');
    const dialog = await screen.findByRole('alertdialog', { name: '¿Eliminar Comparsa Sintética Norte?' });
    expect(
      within(dialog).getByText('No se puede deshacer. 2 jefes de disparo perderán el acceso a ella.'),
    ).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar comparsa' }));

    await expectFocusedNotice('Comparsa eliminada.');
    expect(deleted).toBe(true);
    expect(app.location()).toBe('/comparsas');
  });

  it('never claims that nobody loses access while the FiringChiefs are unknown', async () => {
    const user = userEvent.setup();
    comparsaDetails(NORTE);
    server.use(mock.get(`/api/comparsas/${NORTE.id}/firing-chiefs`, () => problem(500, 'unexpected')));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    await moreAction(user, 'Eliminar comparsa');

    const dialog = await screen.findByRole('alertdialog');
    expect(dialog).toHaveAccessibleDescription(
      'No se puede deshacer. Sus jefes de disparo, si los tiene, perderán el acceso a ella.',
    );
  });

  it('changes nothing when the deletion is dismissed with Escape, and returns focus to "More actions"', async () => {
    const user = userEvent.setup();
    comparsaDetails(NORTE);
    let deleted = false;
    server.use(
      mock.delete(`/api/comparsas/${NORTE.id}`, () => {
        deleted = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    await moreAction(user, 'Eliminar comparsa');
    await screen.findByRole('alertdialog');
    await user.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Más acciones' })).toHaveFocus();
    });
    expect(deleted).toBe(false);
  });

  it('confirms deleting a comparsa without FiringChiefs without counting', async () => {
    const user = userEvent.setup();
    comparsaDetails(NORTE, []);
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    await moreAction(user, 'Eliminar comparsa');

    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByText('No se puede deshacer.')).toBeInTheDocument();
  });

  it('keeps a comparsa in use and says to deactivate it instead', async () => {
    const user = userEvent.setup();
    comparsaDetails(NORTE);
    server.use(mock.delete(`/api/comparsas/${NORTE.id}`, () => problem(409, 'comparsas.inUse')));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    await moreAction(user, 'Eliminar comparsa');
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar comparsa' }));

    expect(
      await within(dialog).findByText(
        'Otros registros usan esta comparsa, así que no se puede eliminar. Desactívala en su lugar.',
      ),
    ).toBeInTheDocument();
  });

  it('keeps a failed deactivation in the dialog and the comparsa active', async () => {
    const user = userEvent.setup();
    comparsaDetails(NORTE);
    server.use(mock.post(`/api/comparsas/${NORTE.id}/deactivate`, () => problem(404, 'comparsas.notFound')));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    await moreAction(user, 'Desactivar comparsa');
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Desactivar comparsa' }));

    expect(await within(dialog).findByText('Esta comparsa no existe o no puedes verla.')).toBeInTheDocument();
    expect(screen.queryByText(/Esta comparsa está inactiva/)).not.toBeInTheDocument();
  });

  it('announces a failed reactivation with focus', async () => {
    const user = userEvent.setup();
    comparsaDetails(OESTE);
    server.use(mock.post(`/api/comparsas/${OESTE.id}/reactivate`, () => problem(500, 'unexpected')));
    await renderApp(`/comparsas/${OESTE.id}`, { session: SYNTHETIC_ADMIN });

    await moreAction(user, 'Reactivar comparsa');

    await expectFocusedNotice('Algo ha fallado. Inténtalo de nuevo.');
    expect(screen.getByText(/Esta comparsa está inactiva/)).toBeInTheDocument();
  });

  it('shows a FiringChief a read-only summary and never asks for Admin data', async () => {
    const requested: string[] = [];
    server.use(
      mock.get(`/api/comparsas/${OESTE.id}`, () => HttpResponse.json(OESTE)),
      mock.get(`/api/comparsas/${OESTE.id}/firing-chiefs`, () => {
        requested.push('firing-chiefs');
        return problem(403, 'forbidden');
      }),
    );
    await renderApp(`/comparsas/${OESTE.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Comparsa Sintética Oeste' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Datos de la comparsa' })).toHaveTextContent('Moro');
    expect(screen.getByText(/Esta comparsa está inactiva/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Editar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Más acciones' })).not.toBeInTheDocument();
    expect(requested).toEqual([]);
  });

  it('shows the not-found page for an unknown or out-of-scope comparsa', async () => {
    server.use(mock.get(`/api/comparsas/${NORTE.id}`, () => problem(404, 'comparsas.notFound')));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Página no encontrada' }),
    ).toBeInTheDocument();
  });

  it('has no accessibility violations, read-only and with the panel open', async () => {
    const user = userEvent.setup();
    comparsaDetails(NORTE, [asFiringChief(CHIEF_UNO)]);
    const { container } = await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    await screen.findByText(CHIEF_UNO.name);

    await waitFor(async () => {
      expect(await axeViolations(container)).toEqual([]);
    });
    const panel = await editData(user);
    await waitFor(async () => {
      expect(await axeViolations(panel)).toEqual([]);
    });
  });
});
