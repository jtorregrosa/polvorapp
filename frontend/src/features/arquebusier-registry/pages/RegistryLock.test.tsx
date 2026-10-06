import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { ARCABUZ, DETAIL_UNO, MODELS, NORTE, ROW_UNO, SUR } from '../test-data';

// Spec "Registry lock screens" (BR-10): the server refuses FiringChief writes while the registry
// is locked; the UI says so and offers none of them. Synthetic data only.

/** `GET /api/registry/lock` answers with the lock, changed by later calls to `set`. */
function lockIs(initial: boolean) {
  let locked = initial;
  server.use(mock.get('/api/registry/lock', () => HttpResponse.json({ locked, changedAt: null })));
  return {
    set: (next: boolean) => {
      locked = next;
    },
  };
}

function registry() {
  server.use(
    mock.get(`/api/arquebusiers/${DETAIL_UNO.id}`, () => HttpResponse.json(DETAIL_UNO)),
    mock.get('/api/comparsas', () => HttpResponse.json([NORTE, SUR])),
    mock.get('/api/arquebusiers', () => HttpResponse.json([ROW_UNO])),
  );
}

const FIRING_CHIEF_NOTICE = /Puedes consultar los arcabuceros, pero no cambiarlos/;
const ADMIN_NOTICE = /Los administradores pueden seguir editándolo/;
const WEAPON_REMOVAL = 'Quitar el arma ARCABUZ MORO DIESTRO nº 1001';
const LOCKED_REASON = 'La Federación ha bloqueado el registro: puedes consultarlo, pero no cambiarlo.';

describe('Registry lock (spec: Registry lock screens)', () => {
  it('lets an Admin lock the registry after confirming, then offers to unlock it', async () => {
    const user = userEvent.setup();
    registry();
    const lock = lockIs(false);
    const { bodies, resolver } = recordBodies(() => {
      lock.set(true);
      return HttpResponse.json({ locked: true, changedAt: '2031-01-10T10:00:00Z' });
    });
    server.use(mock.put('/api/registry/lock', resolver));
    await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });

    // A rarely used action: in "More actions", with the registry's state beside the title (UI audit).
    expect(await screen.findByText('Registro abierto')).toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: 'Más acciones' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Bloquear registro' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Bloquear el registro?' });
    expect(dialog).toHaveTextContent('Los jefes de disparo podrán consultar los arcabuceros');
    await user.click(within(dialog).getByRole('button', { name: 'Bloquear registro' }));

    expect(await screen.findByText(ADMIN_NOTICE)).toBeInTheDocument();
    expect(await screen.findAllByText('Registro bloqueado')).not.toHaveLength(0);
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Más acciones' })).toHaveFocus();
    });
    await user.click(screen.getByRole('button', { name: 'Más acciones' }));
    expect(await screen.findByRole('menuitem', { name: 'Desbloquear registro' })).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(bodies).toEqual([{ locked: true }]);
    // Admins still register while it is locked.
    expect(screen.getByRole('link', { name: 'Registrar arcabucero' })).toBeInTheDocument();
  });

  it('tells a FiringChief on the list and offers no registration', async () => {
    registry();
    lockIs(true);
    await renderApp('/arquebusiers', { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByText(FIRING_CHIEF_NOTICE)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Registrar arcabucero' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Más acciones' })).not.toBeInTheDocument();
  });

  it('shows a FiringChief the detail without edit, status, delete, weapon or photo actions', async () => {
    registry();
    lockIs(true);
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByText(FIRING_CHIEF_NOTICE)).toBeInTheDocument();
    await screen.findByRole('heading', { level: 1 });
    expect(screen.queryByRole('button', { name: /^Editar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Más acciones' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Añadir arma/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Quitar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Elegir|Sustituir/ })).not.toBeInTheDocument();
  });

  it('keeps every action for an Admin on a locked registry, with its own notice', async () => {
    registry();
    lockIs(true);
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_ADMIN });

    expect(await screen.findByText(ADMIN_NOTICE)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Editar datos personales' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Más acciones' })).toBeInTheDocument();
  });

  it('switches to the locked state when a change is refused because the registry was locked meanwhile', async () => {
    const user = userEvent.setup();
    registry();
    const lock = lockIs(false);
    server.use(
      mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, () => {
        lock.set(true);
        return problem(409, 'registry.locked');
      }),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(await screen.findByRole('button', { name: 'Editar datos personales' }));
    const panel = await screen.findByRole('dialog', { name: 'Editar datos personales' });
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(await within(panel).findByText(/La Federación ha bloqueado el registro/)).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(await screen.findByText(FIRING_CHIEF_NOTICE)).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Editar datos personales' })).not.toBeInTheDocument();
    });
  });

  it('says why a registration was refused because the registry was locked meanwhile', async () => {
    const user = userEvent.setup();
    registry();
    lockIs(false);
    server.use(
      mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
      mock.post('/api/arquebusiers', () => problem(409, 'registry.locked')),
    );
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('combobox', { name: 'Comparsa' });

    await user.type(screen.getByLabelText(/ID Unión/), '900001');
    await user.type(screen.getByLabelText(/^DNI\/NIE/), '12345678z');
    await user.type(screen.getByLabelText(/^Nombre/), 'Arcabucera');
    await user.type(screen.getByLabelText(/^Apellidos/), 'Sintética Nueva');
    fireEvent.change(screen.getByLabelText(/Fecha de nacimiento/), { target: { value: '1990-05-01' } });
    await user.click(screen.getByRole('radio', { name: 'Mujer' }));
    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    // A refusal about no field is listed in the error summary.
    expect(await screen.findByRole('group', { name: 'Hay un problema' })).toHaveTextContent(LOCKED_REASON);
  });

  it('says why a new weapon was refused because the registry was locked meanwhile', async () => {
    const user = userEvent.setup();
    registry();
    lockIs(false);
    server.use(
      mock.get('/api/weapon-models', () => HttpResponse.json(MODELS.filter((model) => model.active))),
      mock.post(`/api/arquebusiers/${DETAIL_UNO.id}/owned-weapons`, () => problem(409, 'registry.locked')),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}/weapons/new`, { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('option', { name: ARCABUZ.label });

    await user.selectOptions(screen.getByLabelText(/^Modelo/), ARCABUZ.id);
    await user.type(screen.getByLabelText(/^Nº de arma/), '7');
    await user.type(screen.getByLabelText(/^Nº de guía/), 'SINT-0009');
    await user.click(screen.getByRole('button', { name: 'Añadir arma' }));

    expect(await screen.findByText(LOCKED_REASON)).toBeInTheDocument();
  });

  it('switches to the locked state when removing a weapon is refused because the registry was locked meanwhile', async () => {
    const user = userEvent.setup();
    registry();
    const lock = lockIs(false);
    const [weapon] = DETAIL_UNO.ownedWeapons;
    if (!weapon) throw new Error('DETAIL_UNO has no weapon');
    server.use(
      mock.delete(`/api/arquebusiers/${DETAIL_UNO.id}/owned-weapons/${weapon.id}`, () => {
        lock.set(true);
        return problem(409, 'registry.locked');
      }),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(await screen.findByRole('button', { name: WEAPON_REMOVAL }));
    const confirm = await screen.findByRole('alertdialog');
    await user.click(within(confirm).getByRole('button', { name: 'Quitar' }));

    expect(await within(confirm).findByText(LOCKED_REASON)).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(await screen.findByText(FIRING_CHIEF_NOTICE)).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: WEAPON_REMOVAL })).not.toBeInTheDocument();
    });
  });

  it('has no accessibility violations with the notice and the action', async () => {
    registry();
    lockIs(true);
    const { container } = await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });
    await screen.findByText(ADMIN_NOTICE);
    await screen.findAllByText('Registro bloqueado');

    expect(await axeViolations(container)).toEqual([]);
  });
});
