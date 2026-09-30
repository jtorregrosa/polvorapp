import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { server } from '@/test/server';

const USER_ID = '00000000-0000-4000-8000-000000000003';
const TOKEN = 'synthetic-link-token'; // gitleaks:allow (synthetic test value)
const PASSWORD = 'a synthetic passphrase'; // gitleaks:allow (synthetic test value)
const LINK = `?user=${USER_ID}&token=${TOKEN}`;

async function choosePassword(
  user: ReturnType<typeof userEvent.setup>,
  labels: { password: RegExp; confirm: RegExp },
  submit: string,
  confirmation = PASSWORD,
) {
  await user.type(screen.getByLabelText(labels.password), PASSWORD);
  await user.type(screen.getByLabelText(labels.confirm), confirmation);
  await user.click(screen.getByRole('button', { name: submit }));
}

describe('AcceptInvitationPage (spec: Invitation-only accounts)', () => {
  const labels = { password: /^Contraseña nueva/, confirm: /^Repite la contraseña/ };

  function validInvitation() {
    server.use(
      mock.get('/api/auth/invitations/validate', ({ request }) => {
        const url = new URL(request.url);
        return url.searchParams.get('user') === USER_ID && url.searchParams.get('token') === TOKEN
          ? HttpResponse.json({ name: 'Persona Sintética', email: 'persona@polvorapp.example' })
          : problem(410, 'auth.invalidLink');
      }),
    );
  }

  it('greets the invitee, shows the password rules and continues to enrolment', async () => {
    const user = userEvent.setup();
    validInvitation();
    const accept = recordBodies(() => HttpResponse.json({ next: 'ENROL' }));
    server.use(
      mock.post('/api/auth/invitations/accept', accept.resolver),
      mock.get(
        '/api/auth/enrolment',
        () => HttpResponse.json({ sharedKey: 'JBSWY3DPEHPK3PXP', authenticatorUri: 'otpauth://totp/x' }), // gitleaks:allow (synthetic test value)
      ),
    );
    const app = await renderApp(`/invitations/accept${LINK}`);

    expect(
      await screen.findByText('Hola, Persona Sintética. Crea tu contraseña para persona@polvorapp.example.'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(labels.password)).toHaveAccessibleDescription(
      'Al menos 12 caracteres. Mejor una frase fácil de recordar que una palabra con símbolos.',
    );

    await choosePassword(user, labels, 'Crear contraseña');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Configura la verificación en dos pasos' }),
    ).toBeInTheDocument();
    expect(app.location()).toBe('/enrolment');
    expect(accept.bodies).toEqual([{ userId: USER_ID, token: TOKEN, password: PASSWORD }]);
  });

  it('checks both passwords match before calling the API', async () => {
    const user = userEvent.setup();
    validInvitation();
    await renderApp(`/invitations/accept${LINK}`);
    await screen.findByText(/Hola, Persona Sintética/);

    await choosePassword(user, labels, 'Crear contraseña', 'something else entirely');

    expect(await screen.findByText('Las contraseñas no coinciden.')).toBeInTheDocument();
  });

  it('lists the password rules the API rejected', async () => {
    const user = userEvent.setup();
    validInvitation();
    server.use(
      mock.post('/api/auth/invitations/accept', () =>
        problem(400, 'auth.invalidPassword', { errors: ['PasswordTooCommon', 'PasswordIsEmail'] }),
      ),
    );
    await renderApp(`/invitations/accept${LINK}`);
    await screen.findByText(/Hola, Persona Sintética/);

    await choosePassword(user, labels, 'Crear contraseña');

    expect(await screen.findByText('La contraseña no cumple las reglas.')).toBeInTheDocument();
    expect(screen.getByText('No puede ser tu correo electrónico.')).toBeInTheDocument();
    expect(screen.getByText('Es una contraseña demasiado común. Prueba con una frase.')).toBeInTheDocument();
  });

  it('explains an invalid or used link without a form', async () => {
    validInvitation();
    await renderApp(`/invitations/accept?user=${USER_ID}&token=spent`);

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Esta invitación ya no es válida' }),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText(labels.password)).not.toBeInTheDocument();
  });

  it('does not call a failure to check the link an invalid link', async () => {
    server.use(mock.get('/api/auth/invitations/validate', () => new HttpResponse(null, { status: 503 })));
    await renderApp(`/invitations/accept${LINK}`);

    expect(
      await screen.findByText('No se ha podido completar la acción. Vuelve a intentarlo.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Esta invitación ya no es válida')).not.toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    validInvitation();
    const { container } = await renderApp(`/invitations/accept${LINK}`);
    await screen.findByText(/Hola, Persona Sintética/);

    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('ForgotPasswordPage (spec: Password reset by email)', () => {
  it('shows the same confirmation whatever the answer', async () => {
    const user = userEvent.setup();
    const forgot = recordBodies(() => new HttpResponse(null, { status: 202 }));
    server.use(mock.post('/api/auth/password/forgot', forgot.resolver));
    await renderApp('/password/forgot');

    await user.type(screen.getByLabelText(/^Correo electrónico/), ' persona@polvorapp.example ');
    await user.click(screen.getByRole('button', { name: 'Enviar enlace' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Revisa tu correo' })).toBeInTheDocument();
    expect(
      screen.getByText('Si hay una cuenta con ese correo, recibirás un enlace válido durante una hora.'),
    ).toBeInTheDocument();
    expect(forgot.bodies).toEqual([{ email: 'persona@polvorapp.example' }]);
  });

  it('says when too many links were asked for', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/auth/password/forgot', () => new HttpResponse(null, { status: 429 })));
    await renderApp('/password/forgot');

    await user.type(screen.getByLabelText(/^Correo electrónico/), 'persona@polvorapp.example');
    await user.click(screen.getByRole('button', { name: 'Enviar enlace' }));

    expect(
      await screen.findByText('Demasiadas solicitudes. Espera un momento y vuelve a intentarlo.'),
    ).toBeInTheDocument();
  });

  it('asks for a valid email', async () => {
    const user = userEvent.setup();
    await renderApp('/password/forgot');

    await user.type(screen.getByLabelText(/^Correo electrónico/), 'not-an-email');
    await user.click(screen.getByRole('button', { name: 'Enviar enlace' }));

    expect(await screen.findByText('Escribe un correo válido.')).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderApp('/password/forgot');

    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('ResetPasswordPage (spec: Password reset by email)', () => {
  const labels = { password: /^Contraseña nueva/, confirm: /^Repite la contraseña nueva/ };

  it('sets a new password from the emailed link', async () => {
    const user = userEvent.setup();
    const reset = recordBodies(() => new HttpResponse(null, { status: 204 }));
    server.use(mock.post('/api/auth/password/reset', reset.resolver));
    await renderApp(`/password/reset${LINK}`);

    await choosePassword(user, labels, 'Guardar contraseña');

    expect(
      await screen.findByRole('heading', {
        level: 1,
        name: 'Contraseña cambiada. Ya puedes iniciar sesión.',
      }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Volver al inicio de sesión' })).toHaveAttribute(
      'href',
      '/login',
    );
    expect(reset.bodies).toEqual([{ userId: USER_ID, token: TOKEN, password: PASSWORD }]);
  });

  it('explains an expired link and offers a new one', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/auth/password/reset', () => problem(410, 'auth.invalidLink')));
    await renderApp(`/password/reset${LINK}`);

    await choosePassword(user, labels, 'Guardar contraseña');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Este enlace ya no es válido' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Recuperar la contraseña' })).toHaveAttribute(
      'href',
      '/password/forgot',
    );
  });

  it('treats a link without its parts as invalid', async () => {
    await renderApp('/password/reset?user=only');

    expect(
      screen.getByRole('heading', { level: 1, name: 'Este enlace ya no es válido' }),
    ).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderApp(`/password/reset${LINK}`);

    expect(await axeViolations(container)).toEqual([]);
  });
});
