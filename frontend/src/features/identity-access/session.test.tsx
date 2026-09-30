import { screen, waitFor } from '@testing-library/react';
import { http as mock, HttpResponse } from 'msw';
import { createMemoryRouter, RouterProvider, useLocation } from 'react-router';
import { describe, expect, it } from 'vitest';
import { apiFetch } from '@/api/http';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { RequireAdmin, RequireSession } from './RequireSession';
import { safeReturnTo } from './session';

const ADMIN = {
  id: '1',
  name: 'Admin Sintética',
  email: 'admin@polvorapp.example',
  role: 'ADMIN',
  locale: 'es-ES',
  recoveryCodesLeft: 10,
};
const CHIEF = { ...ADMIN, id: '2', name: 'Jefe Sintético', role: 'FIRING_CHIEF' };

function Location() {
  const location = useLocation();
  return <p data-testid="location">{`${location.pathname}${location.search}`}</p>;
}

function renderRoutes(initial: string) {
  const router = createMemoryRouter(
    [
      { path: '/login', Component: Location },
      {
        Component: RequireSession,
        children: [
          { path: '/', element: <h1>Inicio</h1> },
          { Component: RequireAdmin, children: [{ path: '/users', element: <h1>Usuarios</h1> }] },
        ],
      },
    ],
    { initialEntries: [initial] },
  );
  return renderWithProviders(<RouterProvider router={router} />);
}

describe('RequireSession', () => {
  it('sends a signed-out visitor to sign in with a way back', async () => {
    server.use(mock.get('/api/account', () => HttpResponse.json({ status: 401 }, { status: 401 })));

    await renderRoutes('/users?role=ADMIN');

    expect(await screen.findByTestId('location')).toHaveTextContent(
      `/login?returnTo=${encodeURIComponent('/users?role=ADMIN')}`,
    );
  });

  it('shows the page to a signed-in user', async () => {
    server.use(mock.get('/api/account', () => HttpResponse.json(CHIEF)));

    await renderRoutes('/');

    expect(await screen.findByRole('heading', { name: 'Inicio' })).toBeInTheDocument();
  });

  it('turns a 401 during use into an expired-session sign-in', async () => {
    server.use(
      mock.get('/api/account', () => HttpResponse.json(CHIEF)),
      mock.get('/api/anything', () => HttpResponse.json({ status: 401 }, { status: 401 })),
    );
    await renderRoutes('/');
    await screen.findByRole('heading', { name: 'Inicio' });

    await apiFetch('/api/anything', { method: 'GET' }).catch(() => undefined);

    await waitFor(() => {
      expect(screen.getByTestId('location')).toHaveTextContent('reason=expired');
    });
  });
});

describe('RequireAdmin', () => {
  it('shows "not allowed" to a FiringChief', async () => {
    server.use(mock.get('/api/account', () => HttpResponse.json(CHIEF)));

    await renderRoutes('/users');

    expect(await screen.findByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Usuarios' })).not.toBeInTheDocument();
  });

  it('shows the page to an Admin', async () => {
    server.use(mock.get('/api/account', () => HttpResponse.json(ADMIN)));

    await renderRoutes('/users');

    expect(await screen.findByRole('heading', { name: 'Usuarios' })).toBeInTheDocument();
  });
});

describe('safeReturnTo', () => {
  it.each([
    ['/users?role=ADMIN', '/users?role=ADMIN'],
    ['https://evil.example', '/'],
    ['//evil.example', '/'],
    ['/\\evil.example', '/'],
    ['/users\\x', '/'],
    ['/\tx', '/'],
    ['/users#top', '/users#top'],
    ['/.//evil.example', '/'],
    ['/..//evil.example', '/'],
    ['/a/..//evil.example', '/'],
    ['/%2F%2Fevil.example', '/%2F%2Fevil.example'],
    [null, '/'],
  ])('%s → %s', (value, expected) => {
    expect(safeReturnTo(value)).toBe(expected);
  });
});
