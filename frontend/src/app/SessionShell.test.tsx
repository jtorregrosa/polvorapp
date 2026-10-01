import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';

describe('signed-in shell (platform: Application shell; identity-access: role-based navigation)', () => {
  it('shows the Users entry to an Admin only', async () => {
    await renderApp('/', { session: SYNTHETIC_ADMIN });
    const navigation = screen.getByRole('navigation', { name: 'Navegación principal' });
    expect(within(navigation).getByRole('link', { name: 'Usuarios' })).toHaveAttribute('href', '/users');
    expect(within(navigation).getByRole('link', { name: 'Arcabuceros' })).toHaveAttribute(
      'href',
      '/arquebusiers',
    );
    expect(within(navigation).getByRole('link', { name: 'Comparsas' })).toHaveAttribute('href', '/comparsas');
    expect(within(navigation).getByRole('link', { name: 'Modelos de arma' })).toHaveAttribute(
      'href',
      '/weapon-models',
    );
  });

  it('hides Admin-only entries from a FiringChief', async () => {
    await renderApp('/', { session: SYNTHETIC_FIRING_CHIEF });

    const navigation = screen.getByRole('navigation', { name: 'Navegación principal' });
    expect(within(navigation).getByRole('link', { name: 'Inicio' })).toBeInTheDocument();
    expect(within(navigation).queryByRole('link', { name: 'Usuarios' })).not.toBeInTheDocument();
    expect(within(navigation).getByRole('link', { name: 'Arcabuceros' })).toHaveAttribute(
      'href',
      '/arquebusiers',
    );
    expect(within(navigation).getByRole('link', { name: 'Comparsas' })).toHaveAttribute('href', '/comparsas');
    expect(within(navigation).queryByRole('link', { name: 'Modelos de arma' })).not.toBeInTheDocument();
  });

  it('shows "not allowed" when a FiringChief opens an Admin page, without asking the API', async () => {
    let usersRequested = false;
    server.use(
      mock.get('/api/users', () => {
        usersRequested = true;
        return HttpResponse.json([]);
      }),
    );
    await renderApp('/users', { session: SYNTHETIC_FIRING_CHIEF });

    expect(screen.getByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeInTheDocument();
    expect(screen.getByRole('banner')).toBeInTheDocument();
    expect(usersRequested).toBe(false);
  });

  it('names the user in the user menu and signs out', async () => {
    const user = userEvent.setup();
    let signedOut = false;
    server.use(
      mock.post('/api/auth/logout', () => {
        signedOut = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const app = await renderApp('/', { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(screen.getByRole('button', { name: 'Menú de Jefe Sintético' }));
    expect(screen.getByText('Jefe de disparo')).toBeInTheDocument();
    await user.click(screen.getByRole('menuitem', { name: 'Cerrar sesión' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeInTheDocument();
    expect(app.location()).toBe('/login');
    expect(signedOut).toBe(true);
    expect(app.queryClient.getQueryData(['/api/account'])).toBeNull();
  });

  it('stays signed in and says so when signing out fails', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/auth/logout', () => HttpResponse.error()));
    const app = await renderApp('/', { session: SYNTHETIC_ADMIN });

    await user.click(screen.getByRole('button', { name: 'Menú de Admin Sintética' }));
    await user.click(screen.getByRole('menuitem', { name: 'Cerrar sesión' }));

    expect(
      await screen.findByText(
        'No se ha podido cerrar la sesión. Comprueba la conexión y vuelve a intentarlo.',
      ),
    ).toBeInTheDocument();
    expect(app.location()).toBe('/');
  });

  it('opens the account page from the user menu', async () => {
    const user = userEvent.setup();
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    await user.click(screen.getByRole('button', { name: 'Menú de Admin Sintética' }));
    await user.click(screen.getByRole('menuitem', { name: 'Mi cuenta' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Mi cuenta' })).toBeInTheDocument();
  });

  it('has no accessibility violations on the forbidden page', async () => {
    const { container } = await renderApp('/users', { session: SYNTHETIC_FIRING_CHIEF });

    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('signed-out pages', () => {
  it('render in the public layout, without navigation or user menu', async () => {
    await renderApp('/login');

    expect(screen.getByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeInTheDocument();
    expect(screen.getByRole('main')).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Navegación principal' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Menú de/ })).not.toBeInTheDocument();
  });

  it('send a signed-out visitor of a signed-in page to sign in', async () => {
    const app = await renderApp('/account');

    expect(await screen.findByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeInTheDocument();
    expect(app.location()).toBe(`/login?returnTo=${encodeURIComponent('/account')}`);
  });
});

describe('language (spec: Switch UI language)', () => {
  it('saves a switch as the signed-in user’s preferred language', async () => {
    const user = userEvent.setup();
    const save = recordBodies(() => new HttpResponse(null, { status: 204 }));
    server.use(mock.put('/api/account/locale', save.resolver));
    const app = await renderApp('/', { session: SYNTHETIC_ADMIN });

    await user.selectOptions(screen.getByRole('combobox', { name: 'Idioma' }), 'ca-ES-valencia');

    await waitFor(() => {
      expect(save.bodies).toEqual([{ locale: 'ca-ES-valencia' }]);
    });
    expect(app.i18n.resolvedLanguage).toBe('ca-ES-valencia');
    await waitFor(() => {
      expect(app.queryClient.getQueryData(['/api/account'])).toMatchObject({ locale: 'ca-ES-valencia' });
    });
  });

  it('keeps the new language and says so when it cannot be saved', async () => {
    const user = userEvent.setup();
    server.use(mock.put('/api/account/locale', () => problem(503, 'unavailable')));
    const app = await renderApp('/', { session: SYNTHETIC_ADMIN });

    await user.selectOptions(screen.getByRole('combobox', { name: 'Idioma' }), 'en');

    expect(
      await screen.findByText(
        'The language changed, but it could not be saved as your preference. This browser will keep using it.',
      ),
    ).toBeInTheDocument();
    expect(app.i18n.resolvedLanguage).toBe('en');
  });

  it('does not save anything when signed out', async () => {
    const user = userEvent.setup();
    let saved = false;
    server.use(
      mock.put('/api/account/locale', () => {
        saved = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const app = await renderApp('/login');

    await user.selectOptions(screen.getByRole('combobox', { name: 'Idioma' }), 'en');

    expect(await screen.findByRole('heading', { level: 1, name: 'Sign in' })).toBeInTheDocument();
    expect(app.i18n.resolvedLanguage).toBe('en');
    expect(saved).toBe(false);
  });
});
