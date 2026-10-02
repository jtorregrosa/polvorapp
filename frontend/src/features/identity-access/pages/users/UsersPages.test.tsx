import { screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { UserResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN } from '@/test/identity';
import { server } from '@/test/server';

const CHIEF: UserResponse = {
  id: '00000000-0000-4000-8000-000000000010',
  name: 'Jefa Sintética',
  email: 'jefa@polvorapp.example',
  role: 'FIRING_CHIEF',
  locale: 'ca-ES-valencia',
  status: 'ACTIVE',
  twoFactorEnabled: true,
  lastSignInAt: '2026-05-01T18:30:00Z',
  createdAt: '2026-01-10T09:00:00Z',
};
const INVITED: UserResponse = {
  ...CHIEF,
  id: '00000000-0000-4000-8000-000000000011',
  name: 'Persona Invitada',
  email: 'invitada@polvorapp.example',
  status: 'INVITED',
  twoFactorEnabled: false,
  lastSignInAt: null,
};

function asAdmin(path: string) {
  return renderApp(path, { session: SYNTHETIC_ADMIN });
}

/** `GET /api/users/:id` answers with `user`, updated by later calls to `set`. */
function userDetails(initial: UserResponse) {
  let current = initial;
  server.use(
    mock.get(`/api/users/${initial.id}`, () => HttpResponse.json(current)),
    // The comparsas section of the user page (federation-catalog).
    mock.get(`/api/firing-chiefs/${initial.id}/comparsas`, () => HttpResponse.json([])),
    mock.get('/api/comparsas', () => HttpResponse.json([])),
  );
  return {
    set: (next: Partial<UserResponse>) => {
      current = { ...current, ...next };
    },
  };
}

describe('UsersPage (spec: User management by Admins)', () => {
  it('lists users with role, status, two-step state and last sign-in', async () => {
    server.use(mock.get('/api/users', () => HttpResponse.json([CHIEF, INVITED])));
    await asAdmin('/users');

    const table = await screen.findByRole('table', { name: 'Usuarios' });
    await within(table).findByRole('link', { name: 'Jefa Sintética' });
    const rowOf = (name: string): HTMLElement => {
      const row = within(table).getByRole('link', { name }).closest('tr');
      if (!row) throw new Error(`No row for ${name}`);
      return row;
    };
    const row = rowOf('Jefa Sintética');
    expect(within(row).getByRole('link', { name: 'Jefa Sintética' })).toHaveAttribute(
      'href',
      `/users/${CHIEF.id}`,
    );
    expect(within(row).getByText('Jefe de disparo')).toBeInTheDocument();
    expect(within(row).getByText('Activo')).toBeInTheDocument();
    expect(within(row).getByText('Activada')).toBeInTheDocument();
    const invited = rowOf('Persona Invitada');
    expect(within(invited).getByText('Invitado')).toBeInTheDocument();
    expect(within(invited).getByText('Nunca')).toBeInTheDocument();
  });

  it('filters by role and status through the API and keeps the filters in the address', async () => {
    const user = userEvent.setup();
    const queries: string[] = [];
    server.use(
      mock.get('/api/users', ({ request }) => {
        queries.push(new URL(request.url).search);
        return HttpResponse.json([INVITED]);
      }),
    );
    const app = await asAdmin('/users');
    await screen.findByRole('table', { name: 'Usuarios' });

    await user.selectOptions(screen.getByRole('combobox', { name: 'Estado' }), 'INVITED');
    await user.selectOptions(screen.getByRole('combobox', { name: 'Rol' }), 'FIRING_CHIEF');

    await waitFor(() => {
      expect(queries).toContain('?role=FIRING_CHIEF&status=INVITED');
    });
    expect(app.location()).toBe('/users?status=INVITED&role=FIRING_CHIEF');
  });

  it('shows why the list could not be loaded', async () => {
    server.use(mock.get('/api/users', () => new HttpResponse(null, { status: 500 })));
    await asAdmin('/users');

    expect(
      await screen.findByText('No se ha podido completar la acción. Vuelve a intentarlo.'),
    ).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    server.use(mock.get('/api/users', () => HttpResponse.json([CHIEF, INVITED])));
    const { container } = await asAdmin('/users');
    await screen.findByRole('link', { name: 'Jefa Sintética' });

    expect(await axeViolations(container)).toEqual([]);
  });
});

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

async function editAccount(user: UserEvent) {
  await user.click(await screen.findByRole('button', { name: 'Editar datos de la cuenta' }));
  return screen.findByRole('dialog', { name: 'Editar datos de la cuenta' });
}

describe('InviteUserPage (spec: Invitation-only accounts)', () => {
  async function fillInvitation(user: UserEvent) {
    await user.type(screen.getByLabelText(/^Correo electrónico/), 'nueva@polvorapp.example');
    await user.type(screen.getByLabelText(/^Nombre/), 'Nueva Sintética');
    await user.selectOptions(screen.getByLabelText(/^Idioma de los correos/), 'en');
    await user.click(screen.getByRole('button', { name: 'Enviar invitación' }));
  }

  it('invites a user and opens their page with a confirmation', async () => {
    const user = userEvent.setup();
    const created = { ...INVITED, id: '00000000-0000-4000-8000-000000000012', name: 'Nueva Sintética' };
    const invite = recordBodies(() => HttpResponse.json(created, { status: 201 }));
    server.use(mock.post('/api/users', invite.resolver));
    userDetails(created);
    const app = await asAdmin('/users/new');

    // A FiringChief by default, chosen among radio cards.
    expect(screen.getByRole('radio', { name: 'Jefe de disparo' })).toHaveAttribute('aria-checked', 'true');
    await fillInvitation(user);

    expect(await screen.findByText('Invitación enviada a nueva@polvorapp.example.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Nueva Sintética' })).toBeInTheDocument();
    expect(app.location()).toBe(`/users/${created.id}`);
    expect(invite.bodies).toEqual([
      { email: 'nueva@polvorapp.example', name: 'Nueva Sintética', role: 'FIRING_CHIEF', locale: 'en' },
    ]);
  });

  it('validates the fields before calling the API', async () => {
    const user = userEvent.setup();
    await asAdmin('/users/new');

    await user.click(screen.getByRole('button', { name: 'Enviar invitación' }));

    expect(
      await screen.findAllByText('Este campo es obligatorio.', { selector: '[data-slot="form-message"]' }),
    ).toHaveLength(2);
    await waitFor(() => {
      expect(screen.getByRole('group', { name: 'Hay un problema' })).toHaveFocus();
    });
  });

  it('says on the email field when the email is already taken', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/users', () => problem(409, 'users.emailTaken')));
    await asAdmin('/users/new');

    await fillInvitation(user);

    expect(
      await screen.findByText('Ya existe un usuario con ese correo.', {
        selector: '[data-slot="form-message"]',
      }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/^Correo electrónico/)).toHaveAttribute('aria-invalid', 'true');
  });

  it('continues on the new user’s page when the email could not be sent', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/users', () => problem(502, 'email.sendFailed', { userId: INVITED.id })));
    userDetails(INVITED);
    const app = await asAdmin('/users/new');

    await fillInvitation(user);

    expect(
      await screen.findByText(
        'Los cambios se han guardado, pero no se ha podido enviar el correo. Vuelve a intentarlo desde la ficha del usuario.',
      ),
    ).toBeInTheDocument();
    expect(app.location()).toBe(`/users/${INVITED.id}`);
    expect(screen.getByRole('button', { name: 'Reenviar invitación' })).toBeInTheDocument();
  });

  it('offers to cancel back to the list', async () => {
    await asAdmin('/users/new');

    expect(await screen.findByRole('link', { name: 'Cancelar' })).toHaveAttribute('href', '/users');
  });

  it('has no accessibility violations', async () => {
    const { container } = await asAdmin('/users/new');

    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('UserDetailPage (specs: User management by Admins, Detail pages in read mode)', () => {
  it('shows the user read-only with role, status and two-step state in the header', async () => {
    userDetails(CHIEF);
    await asAdmin(`/users/${CHIEF.id}`);

    const heading = await screen.findByRole('heading', { level: 1, name: 'Jefa Sintética' });
    const header = heading.closest('header');
    expect(header).toHaveTextContent('Jefe de disparo');
    expect(header).toHaveTextContent('Verificación en dos pasos: Activada');
    const account = screen.getByRole('region', { name: 'Datos de la cuenta' });
    expect(account).toHaveTextContent('jefa@polvorapp.example');
    expect(account).toHaveTextContent('Valencià');
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });

  it('edits the name, role and email language in a panel', async () => {
    const user = userEvent.setup();
    const details = userDetails(CHIEF);
    const update = recordBodies(() => {
      details.set({ name: 'Jefa Renombrada', role: 'ADMIN' });
      return HttpResponse.json({ ...CHIEF, name: 'Jefa Renombrada', role: 'ADMIN' });
    });
    server.use(mock.put(`/api/users/${CHIEF.id}`, update.resolver));
    await asAdmin(`/users/${CHIEF.id}`);

    const panel = await editAccount(user);
    const name = within(panel).getByLabelText(/^Nombre/);
    await user.clear(name);
    await user.type(name, 'Jefa Renombrada');
    await user.click(within(panel).getByRole('radio', { name: 'Administrador' }));
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await expectSaved('Cambios guardados');
    expect(await screen.findByRole('heading', { level: 1, name: 'Jefa Renombrada' })).toBeInTheDocument();
    expect(update.bodies).toEqual([{ name: 'Jefa Renombrada', role: 'ADMIN', locale: 'ca-ES-valencia' }]);
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Editar datos de la cuenta' })).toHaveFocus();
    });
  });

  it('keeps the panel open with the last-Admin refusal', async () => {
    const user = userEvent.setup();
    const admin: UserResponse = { ...CHIEF, role: 'ADMIN' };
    userDetails(admin);
    server.use(mock.put(`/api/users/${admin.id}`, () => problem(409, 'users.lastAdmin')));
    await asAdmin(`/users/${admin.id}`);

    const panel = await editAccount(user);
    await user.click(within(panel).getByRole('radio', { name: 'Jefe de disparo' }));
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(await within(panel).findByRole('group', { name: 'Hay un problema' })).toHaveTextContent(
      'Es el único administrador activo: invita o reactiva a otro administrador antes de hacer este cambio.',
    );
  });

  it('deactivates a user from "More actions" only after confirming, set apart as destructive', async () => {
    const user = userEvent.setup();
    const details = userDetails(CHIEF);
    let calls = 0;
    server.use(
      mock.post(`/api/users/${CHIEF.id}/deactivate`, () => {
        calls += 1;
        details.set({ status: 'DEACTIVATED' });
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await asAdmin(`/users/${CHIEF.id}`);

    await user.click(await screen.findByRole('button', { name: 'Más acciones' }));
    const menu = await screen.findByRole('menu');
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent),
    ).toEqual(['Restablecer la verificación en dos pasos', 'Desactivar usuario']);
    expect(within(menu).getByRole('separator')).toBeInTheDocument();
    await user.click(within(menu).getByRole('menuitem', { name: 'Desactivar usuario' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Desactivar a Jefa Sintética?' });
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    expect(calls).toBe(0);
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Más acciones' })).toHaveFocus();
    });

    await moreAction(user, 'Desactivar usuario');
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Desactivar usuario' }),
    );

    await expectSaved('Usuario desactivado.');
    await user.click(screen.getByRole('button', { name: 'Más acciones' }));
    expect(await screen.findByRole('menuitem', { name: 'Reactivar usuario' })).toBeInTheDocument();
    expect(calls).toBe(1);
  });

  it('keeps the dialog open and shows the last-Admin error when deactivation is refused', async () => {
    const user = userEvent.setup();
    userDetails({ ...CHIEF, role: 'ADMIN' });
    server.use(mock.post(`/api/users/${CHIEF.id}/deactivate`, () => problem(409, 'users.lastAdmin')));
    await asAdmin(`/users/${CHIEF.id}`);

    await moreAction(user, 'Desactivar usuario');
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Desactivar usuario' }));

    expect(
      await within(dialog).findByText(
        'Es el único administrador activo: invita o reactiva a otro administrador antes de hacer este cambio.',
      ),
    ).toBeInTheDocument();
  });

  it('refreshes the Admin’s own session after they change their own role', async () => {
    const user = userEvent.setup();
    const self: UserResponse = {
      ...CHIEF,
      id: SYNTHETIC_ADMIN.id,
      name: SYNTHETIC_ADMIN.name,
      role: 'ADMIN',
    };
    const details = userDetails(self);
    server.use(
      mock.put(`/api/users/${self.id}`, () => {
        details.set({ role: 'FIRING_CHIEF' });
        server.use(
          mock.get('/api/account', () => HttpResponse.json({ ...SYNTHETIC_ADMIN, role: 'FIRING_CHIEF' })),
        );
        return HttpResponse.json({ ...self, role: 'FIRING_CHIEF' });
      }),
    );
    await asAdmin(`/users/${self.id}`);
    const navigation = screen.getByRole('navigation', { name: 'Navegación principal', hidden: true });
    expect(within(navigation).getByRole('link', { name: 'Usuarios', hidden: true })).toBeInTheDocument();

    const panel = await editAccount(user);
    await user.click(within(panel).getByRole('radio', { name: 'Jefe de disparo' }));
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(
        within(navigation).queryByRole('link', { name: 'Usuarios', hidden: true }),
      ).not.toBeInTheDocument();
    });
  });

  it('signs the Admin out when their own session cannot be reloaded after changing their role', async () => {
    const user = userEvent.setup();
    const self: UserResponse = {
      ...CHIEF,
      id: SYNTHETIC_ADMIN.id,
      name: SYNTHETIC_ADMIN.name,
      role: 'ADMIN',
    };
    userDetails(self);
    server.use(
      mock.put(`/api/users/${self.id}`, () => {
        server.use(mock.get('/api/account', () => problem(500, 'unexpected')));
        return HttpResponse.json({ ...self, role: 'FIRING_CHIEF' });
      }),
    );
    const app = await asAdmin(`/users/${self.id}`);

    const panel = await editAccount(user);
    await user.click(within(panel).getByRole('radio', { name: 'Jefe de disparo' }));
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    // Never left with privileges it may no longer have.
    await waitFor(() => {
      expect(app.location()).toBe('/login');
    });
  });

  it('resets two-step verification from "More actions" only after confirming', async () => {
    const user = userEvent.setup();
    const details = userDetails(CHIEF);
    server.use(
      mock.post(`/api/users/${CHIEF.id}/two-factor/reset`, () => {
        details.set({ twoFactorEnabled: false });
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await asAdmin(`/users/${CHIEF.id}`);

    await moreAction(user, 'Restablecer la verificación en dos pasos');
    await user.click(
      within(
        await screen.findByRole('alertdialog', {
          name: '¿Restablecer la verificación en dos pasos de Jefa Sintética?',
        }),
      ).getByRole('button', { name: 'Restablecer la verificación en dos pasos' }),
    );

    await expectSaved('Verificación en dos pasos restablecida.');
    await waitFor(() => {
      expect(screen.getByRole('heading', { level: 1 }).closest('header')).toHaveTextContent(
        'Verificación en dos pasos: No configurada',
      );
    });
  });

  it('resends an invitation from the header', async () => {
    const user = userEvent.setup();
    userDetails(INVITED);
    let calls = 0;
    server.use(
      mock.post(`/api/users/${INVITED.id}/invitation`, () => {
        calls += 1;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await asAdmin(`/users/${INVITED.id}`);

    await user.click(await screen.findByRole('button', { name: 'Reenviar invitación' }));

    await expectSaved('Invitación reenviada.');
    expect(calls).toBe(1);
  });

  it('clears an earlier failure once a later action succeeds', async () => {
    const user = userEvent.setup();
    userDetails(INVITED);
    let calls = 0;
    server.use(
      mock.post(`/api/users/${INVITED.id}/invitation`, () => {
        calls += 1;
        return calls === 1 ? problem(500, 'unexpected') : new HttpResponse(null, { status: 204 });
      }),
    );
    await asAdmin(`/users/${INVITED.id}`);

    await user.click(await screen.findByRole('button', { name: 'Reenviar invitación' }));
    const failure = await screen.findByRole('alert');
    await waitFor(() => {
      expect(failure).toHaveFocus();
    });
    await user.click(screen.getByRole('button', { name: 'Reenviar invitación' }));

    await expectSaved('Invitación reenviada.');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('reactivates a deactivated user from "More actions"', async () => {
    const user = userEvent.setup();
    const details = userDetails({ ...CHIEF, status: 'DEACTIVATED' });
    server.use(
      mock.post(`/api/users/${CHIEF.id}/reactivate`, () => {
        details.set({ status: 'ACTIVE' });
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await asAdmin(`/users/${CHIEF.id}`);

    await moreAction(user, 'Reactivar usuario');

    await expectSaved('Usuario reactivado.');
    await user.click(screen.getByRole('button', { name: 'Más acciones' }));
    expect(await screen.findByRole('menuitem', { name: 'Desactivar usuario' })).toBeInTheDocument();
  });

  it('says when the user does not exist', async () => {
    server.use(mock.get('/api/users/missing', () => problem(404, 'users.notFound')));
    await asAdmin('/users/missing');

    expect(await screen.findByText('Este usuario no existe.')).toBeInTheDocument();
  });

  it('has no accessibility violations, read-only and with the panel open', async () => {
    const user = userEvent.setup();
    userDetails(CHIEF);
    const { container } = await asAdmin(`/users/${CHIEF.id}`);
    await screen.findByRole('heading', { level: 1, name: 'Jefa Sintética' });

    expect(await axeViolations(container)).toEqual([]);
    const panel = await editAccount(user);
    await waitFor(async () => {
      expect(await axeViolations(panel)).toEqual([]);
    });
  });
});
