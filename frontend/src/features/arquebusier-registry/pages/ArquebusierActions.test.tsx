import { screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ArquebusierResponse, ComparsaResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { DETAIL_UNO, NORTE, OESTE, SUR } from '../test-data';

// The record actions of the detail page (specs: Transfer between comparsas, Deleting an
// arquebusier, Registry screens, Action hierarchy). Synthetic data only.

function detail(
  current: () => ArquebusierResponse = () => DETAIL_UNO,
  comparsas: ComparsaResponse[] = [NORTE, SUR, OESTE],
) {
  server.use(
    mock.get(`/api/arquebusiers/${DETAIL_UNO.id}`, () => HttpResponse.json(current())),
    mock.get('/api/comparsas', () => HttpResponse.json(comparsas)),
    mock.get('/api/arquebusiers', () => HttpResponse.json([])),
  );
}

async function openMoreActions(user: UserEvent) {
  await user.click(await screen.findByRole('button', { name: 'Más acciones' }));
  return screen.findByRole('menu');
}

describe('Transfer (spec: Transfer between comparsas, Registry screens)', () => {
  it('lets an Admin move the arquebusier to another active comparsa after naming both', async () => {
    const user = userEvent.setup();
    let current = DETAIL_UNO;
    detail(() => current);
    const { bodies, resolver } = recordBodies(() => {
      current = { ...DETAIL_UNO, comparsaId: SUR.id, comparsaName: SUR.name };
      return new HttpResponse(null, { status: 204 });
    });
    server.use(mock.post(`/api/arquebusiers/${DETAIL_UNO.id}/transfer`, resolver));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_ADMIN });

    await user.click(await screen.findByRole('button', { name: 'Trasladar' }));
    const dialog = await screen.findByRole('alertdialog');
    const target = within(dialog).getByRole('combobox', { name: 'Comparsa de destino' });
    await within(dialog).findByRole('option', { name: SUR.name });
    expect(within(target).queryByRole('option', { name: NORTE.name })).not.toBeInTheDocument();
    expect(within(target).queryByRole('option', { name: OESTE.name })).not.toBeInTheDocument();
    await user.selectOptions(target, SUR.id);
    expect(dialog).toHaveAccessibleName(`¿Trasladar a Arcabucero García Sintético a ${SUR.name}?`);
    expect(dialog).toHaveAccessibleDescription(new RegExp(`Pasará de ${NORTE.name} a ${SUR.name}`));
    await user.click(within(dialog).getByRole('button', { name: 'Trasladar' }));

    expect(
      await screen.findByText(`Arcabucero García Sintético pertenece ahora a ${SUR.name}.`),
    ).toBeInTheDocument();
    expect(bodies).toEqual([{ comparsaId: SUR.id }]);
  });

  it('starts on the destination and, confirmed without one, says so at the select (WCAG 3.3.1)', async () => {
    const user = userEvent.setup();
    detail();
    const { bodies, resolver } = recordBodies(() => new HttpResponse(null, { status: 204 }));
    server.use(mock.post(`/api/arquebusiers/${DETAIL_UNO.id}/transfer`, resolver));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_ADMIN });

    await user.click(await screen.findByRole('button', { name: 'Trasladar' }));
    const dialog = await screen.findByRole('alertdialog');
    const target = within(dialog).getByRole('combobox', { name: 'Comparsa de destino' });
    await waitFor(() => {
      expect(target).toHaveFocus();
    });
    await user.click(within(dialog).getByRole('button', { name: 'Trasladar' }));

    expect(dialog).toBeInTheDocument();
    expect(target).toHaveFocus();
    expect(target).toHaveAttribute('aria-invalid', 'true');
    expect(target).toHaveAccessibleDescription('Elige la comparsa de destino para poder trasladar.');
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument();
    expect(bodies).toEqual([]);

    await within(dialog).findByRole('option', { name: SUR.name });
    await user.selectOptions(target, SUR.id);
    expect(target).not.toHaveAttribute('aria-invalid');
    // The new title and description are said without moving focus from the select.
    expect(within(dialog).getByRole('status')).toHaveTextContent(new RegExp(`a ${SUR.name}`));
  });

  it('keeps the dialog open and says why when the target comparsa was deactivated meanwhile', async () => {
    const user = userEvent.setup();
    detail();
    server.use(
      mock.post(`/api/arquebusiers/${DETAIL_UNO.id}/transfer`, () =>
        problem(409, 'arquebusiers.comparsaInactive'),
      ),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_ADMIN });

    await user.click(await screen.findByRole('button', { name: 'Trasladar' }));
    const dialog = await screen.findByRole('alertdialog');
    await within(dialog).findByRole('option', { name: SUR.name });
    await user.selectOptions(within(dialog).getByRole('combobox', { name: 'Comparsa de destino' }), SUR.id);
    await user.click(within(dialog).getByRole('button', { name: 'Trasladar' }));

    expect(
      await within(dialog).findByText('La comparsa está inactiva y no admite nuevos arcabuceros.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  });

  it('is not offered to a FiringChief', async () => {
    detail();
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('button', { name: 'Más acciones' });
    expect(screen.queryByRole('button', { name: 'Trasladar' })).not.toBeInTheDocument();
  });
});

describe('More actions (spec: Registry screens, Action hierarchy)', () => {
  it('moves an active arquebusier to reserve with the version, and says so without moving focus', async () => {
    const user = userEvent.setup();
    let current = DETAIL_UNO;
    detail(() => current);
    const { bodies, resolver } = recordBodies(() => {
      current = { ...DETAIL_UNO, status: 'RESERVE', version: 8 };
      return HttpResponse.json(current);
    });
    server.use(mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, resolver));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const menu = await openMoreActions(user);
    await user.click(within(menu).getByRole('menuitem', { name: 'Pasar a reserva' }));

    await waitFor(() => {
      expect(bodies[0]).toMatchObject({ status: 'RESERVE', version: 7, phone: DETAIL_UNO.phone });
    });
    expect(
      await screen.findByText('Arcabucero García Sintético está ahora en Reserva.', {
        selector: '[role=status]',
      }),
    ).toBeInTheDocument();
  });

  it('sets the destructive action apart', async () => {
    const user = userEvent.setup();
    detail();
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const menu = await openMoreActions(user);

    expect(within(menu).getByRole('separator')).toBeInTheDocument();
    expect(within(menu).getByRole('menuitem', { name: 'Eliminar arcabucero' })).toHaveAttribute(
      'data-variant',
      'destructive',
    );
  });
});

describe('Deletion (spec: Deleting an arquebusier)', () => {
  async function askToDelete(user: UserEvent) {
    const menu = await openMoreActions(user);
    await user.click(within(menu).getByRole('menuitem', { name: 'Eliminar arcabucero' }));
    return screen.findByRole('alertdialog');
  }

  it('deletes after a confirmation that names the person, says it cannot be undone and suggests Reserve', async () => {
    const user = userEvent.setup();
    detail();
    let deleted = false;
    server.use(
      mock.delete(`/api/arquebusiers/${DETAIL_UNO.id}`, () => {
        deleted = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const app = await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const dialog = await askToDelete(user);
    expect(dialog).toHaveAccessibleName('¿Eliminar a Arcabucero García Sintético?');
    expect(dialog).toHaveAccessibleDescription(/^No se puede deshacer.*Reserva/);
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar' }));

    await waitFor(() => {
      expect(app.location()).toBe('/arquebusiers');
    });
    expect(deleted).toBe(true);
    const notice = await screen.findByText('Arcabucero García Sintético se ha eliminado del registro.');
    await waitFor(() => {
      expect(notice.closest('[role="status"], [role="alert"], [tabindex]')).toHaveFocus();
    });
  });

  it('treats an arquebusier someone else already deleted as deleted', async () => {
    const user = userEvent.setup();
    detail();
    server.use(
      mock.delete(`/api/arquebusiers/${DETAIL_UNO.id}`, () => problem(404, 'arquebusiers.notFound')),
    );
    const app = await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const dialog = await askToDelete(user);
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar' }));

    await waitFor(() => {
      expect(app.location()).toBe('/arquebusiers');
    });
    expect(
      await screen.findByText('Arcabucero García Sintético se ha eliminado del registro.'),
    ).toBeInTheDocument();
  });

  it('stays on the arquebusier and says to retry when the registry is busy', async () => {
    const user = userEvent.setup();
    detail();
    server.use(mock.delete(`/api/arquebusiers/${DETAIL_UNO.id}`, () => problem(503, 'registry.busy')));
    const app = await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const dialog = await askToDelete(user);
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar' }));

    expect(await within(dialog).findByText(/El registro está ocupado/)).toBeInTheDocument();
    expect(app.location()).toBe(`/arquebusiers/${DETAIL_UNO.id}`);
  });

  it('changes nothing when the confirmation is cancelled, and returns focus to "More actions"', async () => {
    const user = userEvent.setup();
    detail();
    let deleted = false;
    server.use(
      mock.delete(`/api/arquebusiers/${DETAIL_UNO.id}`, () => {
        deleted = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const app = await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const dialog = await askToDelete(user);
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    expect(deleted).toBe(false);
    expect(app.location()).toBe(`/arquebusiers/${DETAIL_UNO.id}`);
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Más acciones' })).toHaveFocus();
    });
  });

  it('has no accessibility violations with the delete confirmation open', async () => {
    const user = userEvent.setup();
    detail();
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await askToDelete(user);

    expect(await axeViolations(document.body)).toEqual([]);
  });
});
