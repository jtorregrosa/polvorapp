import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN } from '@/test/identity';
import { server } from '@/test/server';

const PASSWORD = 'synthetic-test-passphrase'; // gitleaks:allow (synthetic test value)

/** After the last step, `GET /api/account` answers with the signed-in user. */
function signedInAfterwardsAs(account = SYNTHETIC_ADMIN) {
  server.use(mock.get('/api/account', () => HttpResponse.json(account)));
}

async function submitPassword(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/^Correo electrónico/), 'admin@polvorapp.example');
  await user.type(screen.getByLabelText(/^Contraseña/), PASSWORD);
  await user.click(screen.getByRole('button', { name: 'Continuar' }));
}

describe('SignInPage (spec: Sign-in with two-factor authentication)', () => {
  it('goes on to the code step, keeping the page to return to', async () => {
    const user = userEvent.setup();
    const login = recordBodies(() => HttpResponse.json({ next: 'SECOND_FACTOR' }));
    server.use(mock.post('/api/auth/login', login.resolver));
    const app = await renderApp(`/login?returnTo=${encodeURIComponent('/account')}`);

    await submitPassword(user);

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Verificación en dos pasos' }),
    ).toBeInTheDocument();
    expect(app.location()).toBe(`/login/second-factor?returnTo=${encodeURIComponent('/account')}`);
    expect(login.bodies).toEqual([{ email: 'admin@polvorapp.example', password: PASSWORD }]);
  });

  it('sends a user without two-factor authentication to enrolment', async () => {
    const user = userEvent.setup();
    server.use(
      mock.post('/api/auth/login', () => HttpResponse.json({ next: 'ENROL' })),
      mock.get(
        '/api/auth/enrolment',
        () =>
          HttpResponse.json({
            sharedKey: 'JBSWY3DPEHPK3PXP',
            authenticatorUri: 'otpauth://totp/PolvorApp:x',
          }), // gitleaks:allow (synthetic test value)
      ),
    );
    const app = await renderApp('/login');

    await submitPassword(user);

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Configura la verificación en dos pasos' }),
    ).toBeInTheDocument();
    expect(app.location()).toBe('/enrolment?returnTo=%2F');
  });

  it('shows one generic message for a wrong email or password and clears the password', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/auth/login', () => problem(401, 'auth.invalidCredentials')));
    await renderApp('/login');

    await submitPassword(user);

    expect(await screen.findByText('El correo o la contraseña no son válidos.')).toBeInTheDocument();
    expect(screen.getByLabelText(/^Contraseña/)).toHaveValue('');
    expect(screen.getByLabelText(/^Correo electrónico/)).toHaveValue('admin@polvorapp.example');
  });

  it('explains a lockout', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/auth/login', () => problem(401, 'auth.lockedOut')));
    await renderApp('/login');

    await submitPassword(user);

    expect(
      await screen.findByText('Demasiados intentos fallidos. Espera 15 minutos y vuelve a intentarlo.'),
    ).toBeInTheDocument();
  });

  it('says why when the session expired', async () => {
    await renderApp('/login?reason=expired');

    expect(screen.getByText('Tu sesión ha caducado. Vuelve a iniciar sesión.')).toBeInTheDocument();
  });

  it('validates the fields before calling the API', async () => {
    const user = userEvent.setup();
    await renderApp('/login');

    await user.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(await screen.findAllByText('Este campo es obligatorio.')).toHaveLength(2);
  });

  it('ignores a return address on another site', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/auth/login', () => HttpResponse.json({ next: 'DONE' })));
    const app = await renderApp(`/login?returnTo=${encodeURIComponent('//evil.example/x')}`);
    signedInAfterwardsAs();

    await submitPassword(user);

    await waitFor(() => {
      expect(app.location()).toBe('/');
    });
  });

  it('says so when signed in but the session cannot be loaded', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/auth/login', () => HttpResponse.json({ next: 'DONE' })));
    const app = await renderApp('/login');
    server.use(mock.get('/api/account', () => new HttpResponse(null, { status: 500 })));

    await submitPassword(user);

    expect(
      await screen.findByText('No se ha podido completar la acción. Vuelve a intentarlo.'),
    ).toBeInTheDocument();
    expect(app.location()).toBe('/login');
  });

  it('moves focus into the signed-in page after the last step', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/auth/login', () => HttpResponse.json({ next: 'DONE' })));
    await renderApp(`/login?returnTo=${encodeURIComponent('/account')}`);
    signedInAfterwardsAs();

    await submitPassword(user);

    await screen.findByRole('heading', { level: 1, name: 'Mi cuenta' });
    expect(screen.getByRole('main')).toHaveFocus();
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderApp('/login?reason=expired');

    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('SecondFactorPage (spec: Remembered devices)', () => {
  it('signs in with the code, remembers the device when asked and applies the preferred language', async () => {
    const user = userEvent.setup();
    const secondFactor = recordBodies(() => HttpResponse.json({ next: 'DONE' }));
    server.use(mock.post('/api/auth/login/second-factor', secondFactor.resolver));
    const app = await renderApp(`/login/second-factor?returnTo=${encodeURIComponent('/account')}`);
    signedInAfterwardsAs({ ...SYNTHETIC_ADMIN, locale: 'en' });

    await user.type(screen.getByLabelText(/^Código/), '123 456');
    await user.click(screen.getByRole('checkbox', { name: /Recordar este dispositivo/ }));
    await user.click(screen.getByRole('button', { name: 'Verificar' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'My account' })).toBeInTheDocument();
    expect(app.location()).toBe('/account');
    expect(secondFactor.bodies).toEqual([{ code: '123456', rememberDevice: true }]);
    expect(app.i18n.resolvedLanguage).toBe('en');
  });

  it('does not remember the device by default and says when the code is wrong', async () => {
    const user = userEvent.setup();
    const secondFactor = recordBodies(() => problem(401, 'auth.invalidCode'));
    server.use(mock.post('/api/auth/login/second-factor', secondFactor.resolver));
    await renderApp('/login/second-factor');

    await user.type(screen.getByLabelText(/^Código/), '123456');
    await user.click(screen.getByRole('button', { name: 'Verificar' }));

    expect(
      await screen.findByText('El código no es válido. Compruébalo y vuelve a intentarlo.'),
    ).toBeInTheDocument();
    expect(secondFactor.bodies).toEqual([{ code: '123456', rememberDevice: false }]);
    expect(screen.getByLabelText(/^Código/)).toHaveValue('');
  });

  it('asks for six digits', async () => {
    const user = userEvent.setup();
    await renderApp('/login/second-factor');

    await user.type(screen.getByLabelText(/^Código/), '12345');
    await user.click(screen.getByRole('button', { name: 'Verificar' }));

    expect(await screen.findByText('Escribe las 6 cifras del código.')).toBeInTheDocument();
  });

  it('goes back to the password step when the pending sign-in expired', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/auth/login/second-factor', () => problem(401, 'auth.stepExpired')));
    const app = await renderApp(`/login/second-factor?returnTo=${encodeURIComponent('/account')}`);

    await user.type(screen.getByLabelText(/^Código/), '123456');
    await user.click(screen.getByRole('button', { name: 'Verificar' }));

    const notice = await screen.findByText('El paso ha caducado. Vuelve a iniciar sesión.');
    expect(notice.closest('[data-severity]')).toHaveFocus();
    expect(app.location()).toBe(`/login?returnTo=${encodeURIComponent('/account')}&reason=stepExpired`);
  });

  it('offers a recovery code instead', async () => {
    const user = userEvent.setup();
    const app = await renderApp('/login/second-factor?returnTo=%2Faccount');

    await user.click(screen.getByRole('link', { name: 'No tengo el móvil: usar un código de recuperación' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Código de recuperación' }),
    ).toBeInTheDocument();
    expect(app.location()).toBe('/login/recovery-code?returnTo=%2Faccount');
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderApp('/login/second-factor');

    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('RecoveryCodePage', () => {
  it('signs in with a recovery code and warns when few are left', async () => {
    const user = userEvent.setup();
    const recovery = recordBodies(() => HttpResponse.json({ next: 'DONE', recoveryCodesLeft: 1 }));
    server.use(mock.post('/api/auth/login/recovery-code', recovery.resolver));
    const app = await renderApp('/login/recovery-code?returnTo=%2Faccount');
    signedInAfterwardsAs();

    await user.type(screen.getByLabelText(/^Código de recuperación/), ' abcd-efgh ');
    await user.click(screen.getByRole('button', { name: 'Verificar' }));

    expect(
      await screen.findByText('Te queda 1 código de recuperación. Genera códigos nuevos desde Mi cuenta.'),
    ).toBeInTheDocument();
    expect(recovery.bodies).toEqual([{ code: 'abcd-efgh' }]);

    await user.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Mi cuenta' })).toBeInTheDocument();
    expect(app.location()).toBe('/account');
  });

  it('lets the person continue again when the session cannot be loaded', async () => {
    const user = userEvent.setup();
    server.use(
      mock.post('/api/auth/login/recovery-code', () =>
        HttpResponse.json({ next: 'DONE', recoveryCodesLeft: 8 }),
      ),
    );
    const app = await renderApp('/login/recovery-code?returnTo=%2Faccount');
    server.use(mock.get('/api/account', () => new HttpResponse(null, { status: 500 })));
    await user.type(screen.getByLabelText(/^Código de recuperación/), 'abcd-efgh');
    await user.click(screen.getByRole('button', { name: 'Verificar' }));
    await screen.findByText(
      'Te quedan 8 códigos de recuperación. Si te quedan pocos, genera códigos nuevos desde Mi cuenta.',
    );

    await user.click(screen.getByRole('button', { name: 'Continuar' }));
    expect(
      await screen.findByText('No se ha podido completar la acción. Vuelve a intentarlo.'),
    ).toBeInTheDocument();
    signedInAfterwardsAs();
    await user.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Mi cuenta' })).toBeInTheDocument();
    expect(app.location()).toBe('/account');
  });

  it('says when the recovery code is wrong', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/auth/login/recovery-code', () => problem(401, 'auth.invalidCode')));
    await renderApp('/login/recovery-code');

    await user.type(screen.getByLabelText(/^Código de recuperación/), 'wrong-code');
    await user.click(screen.getByRole('button', { name: 'Verificar' }));

    expect(
      await screen.findByText('El código no es válido. Compruébalo y vuelve a intentarlo.'),
    ).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderApp('/login/recovery-code');

    expect(await axeViolations(container)).toEqual([]);
  });
});
