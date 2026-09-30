import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
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

describe('ComparsaFormPage (spec: Comparsa management by Admins)', () => {
  it('creates a comparsa and continues on its page with a notice', async () => {
    const user = userEvent.setup();
    const created: ComparsaResponse = { ...NORTE, name: 'Comparsa Sintética Nueva' };
    comparsaDetails(created);
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(created, { status: 201 }));
    server.use(mock.post('/api/comparsas', resolver));
    const app = await renderApp('/comparsas/new', { session: SYNTHETIC_ADMIN });

    await user.type(await screen.findByRole('textbox', { name: /Nombre/ }), 'Comparsa Sintética Nueva');
    await user.selectOptions(screen.getByRole('combobox', { name: /Bando/ }), 'CHRISTIAN');
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
    await user.selectOptions(screen.getByRole('combobox', { name: /Bando/ }), 'CHRISTIAN');
    await user.click(screen.getByRole('button', { name: 'Crear comparsa' }));

    const message = await screen.findByText('Ya existe una comparsa con ese nombre.');
    expect(message).toHaveAttribute('data-slot', 'form-message');
    await waitFor(() => {
      expect(screen.getByRole('textbox', { name: /Nombre/ })).toHaveFocus();
    });
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

describe('ComparsaDetailPage (specs: Comparsa management by Admins, Deleting comparsas)', () => {
  it('lets an Admin edit the name and side', async () => {
    const user = userEvent.setup();
    const details = comparsaDetails(NORTE);
    const { bodies, resolver } = recordBodies(() => {
      details.set({ name: 'Comparsa Sintética Nord', side: 'MOORISH' });
      return HttpResponse.json({ ...NORTE, name: 'Comparsa Sintética Nord', side: 'MOORISH' });
    });
    server.use(mock.put(`/api/comparsas/${NORTE.id}`, resolver));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    const name = await screen.findByRole('textbox', { name: /Nombre/ });
    expect(name).toHaveValue('Comparsa Sintética Norte');
    await user.clear(name);
    await user.type(name, 'Comparsa Sintética Nord');
    await user.selectOptions(screen.getByRole('combobox', { name: /Bando/ }), 'MOORISH');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await expectFocusedNotice('Cambios guardados.');
    expect(bodies).toEqual([{ name: 'Comparsa Sintética Nord', side: 'MOORISH' }]);
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Comparsa Sintética Nord' }),
    ).toBeInTheDocument();
  });

  it('deactivates after confirmation and reactivates', async () => {
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

    await user.click(await screen.findByRole('button', { name: 'Desactivar comparsa' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Desactivar Comparsa Sintética Norte?' });
    await user.click(within(dialog).getByRole('button', { name: 'Desactivar comparsa' }));

    await expectFocusedNotice('Comparsa desactivada.');
    expect(await screen.findByText(/Esta comparsa está inactiva/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reactivar comparsa' }));
    await expectFocusedNotice('Comparsa reactivada.');
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

    await user.click(await screen.findByRole('button', { name: 'Eliminar comparsa' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Eliminar Comparsa Sintética Norte?' });
    expect(
      within(dialog).getByText('No se puede deshacer. 2 jefes de disparo perderán el acceso a ella.'),
    ).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar comparsa' }));

    await expectFocusedNotice('Comparsa eliminada.');
    expect(deleted).toBe(true);
    expect(app.location()).toBe('/comparsas');
  });

  it('confirms deleting a comparsa without FiringChiefs without counting', async () => {
    const user = userEvent.setup();
    comparsaDetails(NORTE, []);
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    await user.click(await screen.findByRole('button', { name: 'Eliminar comparsa' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByText('No se puede deshacer.')).toBeInTheDocument();
  });

  it('keeps a comparsa in use and says to deactivate it instead', async () => {
    const user = userEvent.setup();
    comparsaDetails(NORTE);
    server.use(mock.delete(`/api/comparsas/${NORTE.id}`, () => problem(409, 'comparsas.inUse')));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    await user.click(await screen.findByRole('button', { name: 'Eliminar comparsa' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar comparsa' }));

    expect(
      await within(dialog).findByText(
        'Otros registros usan esta comparsa, así que no se puede eliminar. Desactívala en su lugar.',
      ),
    ).toBeInTheDocument();
  });

  it('shows a duplicate name on the name field when an edit is rejected', async () => {
    const user = userEvent.setup();
    comparsaDetails(NORTE);
    server.use(mock.put(`/api/comparsas/${NORTE.id}`, () => problem(409, 'comparsas.nameTaken')));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    const name = await screen.findByRole('textbox', { name: /Nombre/ });
    await waitFor(() => {
      expect(name).toHaveValue(NORTE.name);
    });
    await user.clear(name);
    await user.type(name, 'Comparsa Sintética Sur');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByText('Ya existe una comparsa con ese nombre.')).toHaveAttribute(
      'data-slot',
      'form-message',
    );
    await waitFor(() => {
      expect(name).toHaveFocus();
    });
    expect(screen.getByRole('heading', { level: 1, name: NORTE.name })).toBeInTheDocument();
  });

  it('keeps a failed deactivation in the dialog and the comparsa active', async () => {
    const user = userEvent.setup();
    comparsaDetails(NORTE);
    server.use(mock.post(`/api/comparsas/${NORTE.id}/deactivate`, () => problem(404, 'comparsas.notFound')));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    await user.click(await screen.findByRole('button', { name: 'Desactivar comparsa' }));
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

    await user.click(await screen.findByRole('button', { name: 'Reactivar comparsa' }));

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
    expect(screen.getByText('Moro')).toBeInTheDocument();
    expect(screen.getByText(/Esta comparsa está inactiva/)).toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Eliminar comparsa' })).not.toBeInTheDocument();
    expect(requested).toEqual([]);
  });

  it('shows the not-found page for an unknown or out-of-scope comparsa', async () => {
    server.use(mock.get(`/api/comparsas/${NORTE.id}`, () => problem(404, 'comparsas.notFound')));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Página no encontrada' }),
    ).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    comparsaDetails(NORTE, [asFiringChief(CHIEF_UNO)]);
    const { container } = await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    await screen.findByRole('textbox', { name: /Nombre/ });

    await waitFor(async () => {
      expect(await axeViolations(container)).toEqual([]);
    });
  });
});
