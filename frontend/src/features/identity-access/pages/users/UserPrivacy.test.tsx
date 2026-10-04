import { screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { UserResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN } from '@/test/identity';
import { server } from '@/test/server';

// Specs audit-privacy "GDPR request screens" (users) and identity-access "User management by
// Admins" (ERASED). Synthetic data only.
const CHIEF: UserResponse = {
  id: '00000000-0000-4000-8000-000000000010',
  name: 'Jefa Sintética',
  email: 'jefa@polvorapp.example',
  role: 'FIRING_CHIEF',
  locale: 'es-ES',
  status: 'ACTIVE',
  twoFactorEnabled: true,
  lastSignInAt: '2030-05-01T18:30:00Z',
  createdAt: '2030-01-10T09:00:00Z',
};
// What the API returns once the user is erased (design D8).
const ERASED: UserResponse = {
  ...CHIEF,
  name: '—',
  email: 'erased-00000000000040008000000000000010@erased.invalid',
  status: 'ERASED',
  twoFactorEnabled: false,
  lastSignInAt: null,
};

let saved: string | undefined;

beforeEach(() => {
  saved = undefined;
  vi.stubGlobal(
    'URL',
    class extends URL {
      static override createObjectURL = vi.fn(() => 'blob:personal-data');
      static override revokeObjectURL = vi.fn();
    },
  );
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    saved = this.download;
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

/** `GET /api/users/:id` answers with the user, updated by later calls to `set`. */
function userDetails(initial: UserResponse) {
  let current = initial;
  server.use(
    mock.get(`/api/users/${initial.id}`, () => HttpResponse.json(current)),
    mock.get(`/api/firing-chiefs/${initial.id}/comparsas`, () => HttpResponse.json([])),
    mock.get('/api/comparsas', () => HttpResponse.json([])),
  );
  return {
    set: (next: UserResponse) => {
      current = next;
    },
  };
}

async function moreAction(user: UserEvent, name: string): Promise<void> {
  await user.click(await screen.findByRole('button', { name: 'Más acciones' }));
  await user.click(await screen.findByRole('menuitem', { name }));
}

const asAdmin = (path: string) => renderApp(path, { session: SYNTHETIC_ADMIN });

describe('User privacy requests (spec: GDPR request screens)', () => {
  it('downloads a user’s personal data from "More actions" with a reference', async () => {
    const user = userEvent.setup();
    userDetails(CHIEF);
    const exported = recordBodies(
      () =>
        new HttpResponse(new Uint8Array([0x50, 0x4b]), {
          headers: { 'Content-Disposition': 'attachment; filename=polvorapp-personal-data-20300714.zip' },
        }),
    );
    server.use(mock.post(`/api/privacy/users/${CHIEF.id}/export`, exported.resolver));
    await asAdmin(`/users/${CHIEF.id}`);

    await moreAction(user, 'Descargar datos personales');
    const dialog = await screen.findByRole('alertdialog', { name: 'Descargar los datos personales' });
    const reference = within(dialog).getByRole('textbox', { name: 'Referencia de la solicitud' });
    expect(reference).toHaveFocus();
    await user.type(reference, 'REQ-2030-09');
    await user.click(within(dialog).getByRole('button', { name: 'Descargar' }));

    await waitFor(() => {
      expect(saved).toBe('polvorapp-personal-data-20300714.zip');
    });
    expect(exported.bodies).toEqual([{ reference: 'REQ-2030-09' }]);
  });

  it('erases a user after confirming, then shows them as erased with only "View history"', async () => {
    const user = userEvent.setup();
    const details = userDetails(CHIEF);
    const erased = recordBodies(() => {
      details.set(ERASED);
      return HttpResponse.json({ counts: { usersErased: 1, assignmentsRemoved: 2 }, filesPending: 0 });
    });
    server.use(mock.post(`/api/privacy/users/${CHIEF.id}/erasure`, erased.resolver));
    await asAdmin(`/users/${CHIEF.id}`);

    await moreAction(user, 'Borrar datos personales');
    const dialog = await screen.findByRole('alertdialog', { name: '¿Borrar los datos de Jefa Sintética?' });
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toHaveFocus();
    expect(dialog).toHaveTextContent('No se puede deshacer.');
    expect(dialog).toHaveTextContent('Usuario borrado');
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Referencia de la solicitud' }),
      'REQ-2030-10',
    );
    await user.click(within(dialog).getByRole('button', { name: 'Borrar' }));

    expect(
      await screen.findByText(/Datos borrados\. 1 cuenta de usuario borrada y 2 asignaciones/),
    ).toBeInTheDocument();
    expect(erased.bodies).toEqual([{ reference: 'REQ-2030-10' }]);
    expect(await screen.findByRole('heading', { level: 1, name: 'Usuario borrado' })).toBeInTheDocument();
  });

  it('shows a refusal in the dialog, e.g. the caller erasing themselves', async () => {
    const user = userEvent.setup();
    userDetails(CHIEF);
    server.use(
      mock.post(`/api/privacy/users/${CHIEF.id}/erasure`, () => problem(409, 'privacy.selfErasure')),
    );
    await asAdmin(`/users/${CHIEF.id}`);

    await moreAction(user, 'Borrar datos personales');
    const dialog = await screen.findByRole('alertdialog');
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Referencia de la solicitud' }),
      'REQ-2030-11',
    );
    await user.click(within(dialog).getByRole('button', { name: 'Borrar' }));

    expect(
      await within(dialog).findByText('No puedes borrar tus propios datos. Pídeselo a otro administrador.'),
    ).toBeInTheDocument();
  });
});

describe('Erased users (spec: User management by Admins)', () => {
  it('shows an erased user with the ERASED status, no edits and only "View history"', async () => {
    userDetails(ERASED);
    const { container } = await asAdmin(`/users/${ERASED.id}`);

    const heading = await screen.findByRole('heading', { level: 1, name: 'Usuario borrado' });
    expect(heading.closest('header')).toHaveTextContent('Borrado');
    expect(screen.getByRole('link', { name: 'Ver historial' })).toHaveAttribute(
      'href',
      `/audit-log?entityType=User&entityId=${ERASED.id}`,
    );
    expect(screen.queryByRole('button', { name: 'Más acciones' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Editar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Comparsas' })).not.toBeInTheDocument();
    // The API's placeholder address is not theirs: shown as not given.
    expect(screen.getByRole('region', { name: 'Datos de la cuenta' })).not.toHaveTextContent(
      'erased.invalid',
    );
    expect(await axeViolations(container)).toEqual([]);
  });

  it('filters the users list by ERASED and names erased users with the marker', async () => {
    const user = userEvent.setup();
    const queries: string[] = [];
    server.use(
      mock.get('/api/users', ({ request }) => {
        queries.push(new URL(request.url).search);
        return HttpResponse.json(queries.length > 1 ? [ERASED] : [CHIEF]);
      }),
    );
    await asAdmin('/users');
    await screen.findByRole('link', { name: 'Jefa Sintética' });

    await user.selectOptions(screen.getByRole('combobox', { name: 'Estado' }), 'ERASED');

    const table = screen.getByRole('table', { name: 'Usuarios' });
    expect(await within(table).findByRole('link', { name: 'Usuario borrado' })).toHaveAttribute(
      'href',
      `/users/${ERASED.id}`,
    );
    expect(within(table).getByText('Borrado')).toBeInTheDocument();
    expect(queries).toContain('?status=ERASED');
  });
});
