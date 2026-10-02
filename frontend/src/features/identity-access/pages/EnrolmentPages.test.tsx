import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN } from '@/test/identity';
import { server } from '@/test/server';

const SHARED_KEY = 'JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP'; // gitleaks:allow (synthetic test value)
const CODES = ['aaaa-bbbb', 'cccc-dddd', 'eeee-ffff'];

function enrolment() {
  let requests = 0;
  server.use(
    mock.get('/api/auth/enrolment', () => {
      requests += 1;
      return HttpResponse.json({
        sharedKey: SHARED_KEY,
        authenticatorUri: `otpauth://totp/PolvorApp:admin%40polvorapp.example?secret=${SHARED_KEY}&issuer=PolvorApp`,
      });
    }),
  );
  return { requests: () => requests };
}

describe('EnrolmentPage (spec: Mandatory two-factor enrolment)', () => {
  it('shows the QR code and the key to type, then the recovery codes once', async () => {
    const user = userEvent.setup();
    const key = enrolment();
    const confirm = recordBodies(() => HttpResponse.json({ recoveryCodes: CODES }));
    server.use(mock.post('/api/auth/enrolment', confirm.resolver));
    const app = await renderApp('/enrolment?returnTo=%2Faccount');

    await waitFor(() => {
      expect(
        screen.getByRole('img', { name: 'Código QR para configurar la aplicación de autenticación' }),
      ).toHaveAttribute('src', expect.stringMatching(/^data:image\/svg\+xml/));
    });
    expect(screen.getByText(SHARED_KEY)).toBeInTheDocument();

    await user.type(screen.getByLabelText(/^Código de la aplicación/), '123-456');
    await user.click(screen.getByRole('button', { name: 'Activar' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Guarda tus códigos de recuperación' }),
    ).toBeInTheDocument();
    for (const code of CODES) {
      expect(screen.getByText(code)).toBeInTheDocument();
    }
    expect(confirm.bodies).toEqual([{ code: '123456' }]);
    expect(app.location()).toBe('/recovery-codes');
    expect(JSON.stringify(app.router.state.location.state)).not.toContain(CODES[0]);
    expect(key.requests()).toBe(1);
  });

  it('says when the code is wrong and lets the person try again with the same key', async () => {
    const user = userEvent.setup();
    const key = enrolment();
    server.use(mock.post('/api/auth/enrolment', () => problem(400, 'auth.invalidCode')));
    await renderApp('/enrolment');
    await screen.findByText(SHARED_KEY);

    await user.type(screen.getByLabelText(/^Código de la aplicación/), '000000');
    await user.click(screen.getByRole('button', { name: 'Activar' }));

    expect(
      await screen.findByText('El código no es válido. Compruébalo y vuelve a intentarlo.'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/^Código de la aplicación/)).toHaveValue('');
    expect(screen.getByText(SHARED_KEY)).toBeInTheDocument();
    expect(key.requests()).toBe(1);
  });

  it('sends the person back to sign in when there is no pending sign-in', async () => {
    server.use(mock.get('/api/auth/enrolment', () => problem(401, 'auth.stepExpired')));
    await renderApp('/enrolment');

    expect(await screen.findByText('El paso ha caducado. Vuelve a iniciar sesión.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Volver al inicio de sesión' })).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    enrolment();
    const { container } = await renderApp('/enrolment');
    await screen.findByText(SHARED_KEY);

    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('RecoveryCodesPage', () => {
  async function showCodes(returnTo = '/account') {
    const user = userEvent.setup();
    enrolment();
    server.use(mock.post('/api/auth/enrolment', () => HttpResponse.json({ recoveryCodes: CODES })));
    const app = await renderApp(`/enrolment?returnTo=${encodeURIComponent(returnTo)}`);
    await screen.findByText(SHARED_KEY);
    await user.type(screen.getByLabelText(/^Código de la aplicación/), '123456');
    await user.click(screen.getByRole('button', { name: 'Activar' }));
    await screen.findByText(CODES[0] ?? '');
    return { app, user };
  }

  it('continues to the page the person came for once they are saved', async () => {
    const { app, user } = await showCodes();
    server.use(mock.get('/api/account', () => HttpResponse.json(SYNTHETIC_ADMIN)));

    await user.click(screen.getByRole('button', { name: 'Ya los he guardado' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Mi cuenta' })).toBeInTheDocument();
    expect(app.location()).toBe('/account');
  });

  it('keeps the codes on screen when the session cannot be loaded', async () => {
    const { user } = await showCodes();
    server.use(mock.get('/api/account', () => new HttpResponse(null, { status: 503 })));

    await user.click(screen.getByRole('button', { name: 'Ya los he guardado' }));

    expect(
      await screen.findByText('No se ha podido completar la acción. Vuelve a intentarlo.'),
    ).toBeInTheDocument();
    expect(screen.getByText(CODES[0] ?? '')).toBeInTheDocument();
  });

  it('does not show the codes again after leaving the page and coming back', async () => {
    const { app } = await showCodes();

    await act(() => app.router.navigate('/password/forgot'));
    await act(() => app.router.navigate('/recovery-codes'));

    await waitFor(() => {
      expect(app.location()).not.toBe('/recovery-codes');
    });
    expect(screen.queryByText(CODES[0] ?? '')).not.toBeInTheDocument();
  });

  it('does not show codes again when opened directly', async () => {
    const app = await renderApp('/recovery-codes');

    await waitFor(() => {
      expect(app.location()).not.toBe('/recovery-codes');
    });
    expect(screen.queryByText(CODES[0] ?? '')).not.toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    await showCodes();

    expect(await axeViolations(document.body)).toEqual([]);
  });
});
