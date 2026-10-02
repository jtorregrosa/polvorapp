import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';

const CURRENT = 'the current passphrase'; // gitleaks:allow (synthetic test value)
const NEXT = 'a brand new passphrase'; // gitleaks:allow (synthetic test value)

function openAccount() {
  return renderApp('/account', { session: SYNTHETIC_FIRING_CHIEF });
}

async function changePassword(user: ReturnType<typeof userEvent.setup>, confirmation = NEXT) {
  await user.type(screen.getByLabelText(/^Contraseña actual/), CURRENT);
  await user.type(screen.getByLabelText(/^Contraseña nueva/), NEXT);
  await user.type(screen.getByLabelText(/^Repite la contraseña nueva/), confirmation);
  await user.click(screen.getByRole('button', { name: 'Cambiar contraseña' }));
}

describe('AccountPage (spec: Account self-service)', () => {
  it('shows the user’s details and remaining recovery codes', async () => {
    await openAccount();

    expect(screen.getByRole('heading', { level: 1, name: 'Mi cuenta' })).toBeInTheDocument();
    // Spec: the account page is in sections (profile, password, two-step verification, sessions).
    const profile = screen.getByRole('region', { name: 'Datos' });
    expect(profile).toHaveTextContent('jefe@polvorapp.example');
    expect(profile).toHaveTextContent('Jefe de disparo');
    expect(screen.getByRole('region', { name: 'Cambiar la contraseña' })).toBeInTheDocument();
    expect(screen.getByText('Te quedan 10 códigos sin usar.')).toBeInTheDocument();
  });

  it('changes the password', async () => {
    const user = userEvent.setup();
    const change = recordBodies(() => new HttpResponse(null, { status: 204 }));
    server.use(mock.post('/api/account/password', change.resolver));
    await openAccount();

    await changePassword(user);

    // Announced politely, without moving focus (SC 4.1.3).
    await waitFor(() => {
      expect(
        screen.getByText('Contraseña cambiada. Las demás sesiones se han cerrado.', {
          selector: '[role=status]',
        }),
      ).toBeInTheDocument();
    });
    expect(change.bodies).toEqual([{ currentPassword: CURRENT, newPassword: NEXT }]);
    expect(screen.getByLabelText(/^Contraseña nueva/)).toHaveValue('');
  });

  it('says when the current password is wrong', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/account/password', () => problem(400, 'account.wrongCurrentPassword')));
    await openAccount();

    await changePassword(user);

    expect(await screen.findByText('La contraseña actual no es correcta.')).toBeInTheDocument();
    expect(screen.getByLabelText(/^Contraseña actual/)).toHaveValue('');
  });

  it('checks the new password before calling the API', async () => {
    const user = userEvent.setup();
    await openAccount();

    await changePassword(user, 'something different');

    expect(await screen.findByText('Las contraseñas no coinciden.')).toBeInTheDocument();
  });

  it('regenerates the recovery codes with a code from the app and shows them once', async () => {
    const user = userEvent.setup();
    const regenerate = recordBodies(() => HttpResponse.json({ recoveryCodes: ['aaaa-bbbb', 'cccc-dddd'] }));
    server.use(mock.post('/api/account/recovery-codes', regenerate.resolver));
    await openAccount();

    await user.type(screen.getByLabelText(/^Código de la aplicación de autenticación/), '654 321');
    await user.click(screen.getByRole('button', { name: 'Generar códigos nuevos' }));

    expect(
      await screen.findByText('Códigos nuevos generados. Los anteriores ya no sirven.'),
    ).toBeInTheDocument();
    expect(screen.getByText('aaaa-bbbb')).toBeInTheDocument();
    expect(regenerate.bodies).toEqual([{ code: '654321' }]);
  });

  it('says when the regeneration code is wrong', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/account/recovery-codes', () => problem(400, 'auth.invalidCode')));
    await openAccount();

    await user.type(screen.getByLabelText(/^Código de la aplicación de autenticación/), '000000');
    await user.click(screen.getByRole('button', { name: 'Generar códigos nuevos' }));

    expect(
      await screen.findByText('El código no es válido. Compruébalo y vuelve a intentarlo.'),
    ).toBeInTheDocument();
  });

  it('signs out everywhere only after confirming', async () => {
    const user = userEvent.setup();
    let calls = 0;
    server.use(
      mock.post('/api/account/sign-out-everywhere', () => {
        calls += 1;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const app = await openAccount();

    await user.click(screen.getByRole('button', { name: 'Cerrar todas las sesiones' }));
    const dialog = screen.getByRole('alertdialog', { name: '¿Cerrar la sesión en todos tus dispositivos?' });
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    expect(calls).toBe(0);

    await user.click(screen.getByRole('button', { name: 'Cerrar todas las sesiones' }));
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Cerrar todas las sesiones' }),
    );

    expect(await screen.findByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeInTheDocument();
    expect(app.location()).toBe('/login');
    expect(calls).toBe(1);
  });

  it('has no accessibility violations', async () => {
    const { container } = await openAccount();

    expect(await axeViolations(container)).toEqual([]);
  });
});
