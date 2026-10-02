import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { createMemoryRouter, RouterProvider, type RouteObject } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN } from '@/test/identity';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { RequireSession } from '@/features/identity-access/RequireSession';
import { appRoutes } from './routes';

function Boom(): never {
  throw new Error('render failure sentinel');
}

/** The signed-in branch of the route table: session guard → shell → pages. */
function signedInRoutes(): { guard: RouteObject; shell: RouteObject; pages: RouteObject } {
  const guard = appRoutes.find((route) => route.Component === RequireSession);
  const shell = guard?.children?.[0];
  const pages = shell?.children?.[0];
  if (!guard || !shell || !pages || pages.index) throw new Error('Unexpected route table shape');
  return { guard, shell, pages };
}

/** The signed-in route table with an extra page that throws while rendering. */
function routesWithFailingPage(): RouteObject[] {
  const { guard, shell, pages } = signedInRoutes();
  const failingPages: RouteObject = {
    ...pages,
    index: false,
    children: [...(pages.children ?? []), { path: 'boom', Component: Boom }],
  };
  return [{ ...guard, index: false, children: [{ ...shell, index: false, children: [failingPages] }] }];
}

/** The signed-in route table whose shell throws while rendering. */
function routesWithFailingShell(): RouteObject[] {
  const { guard, shell } = signedInRoutes();
  return [{ ...guard, index: false, children: [{ ...shell, index: false, Component: Boom }] }];
}

function renderSignedIn(router: ReturnType<typeof createMemoryRouter>, language = 'es-ES') {
  return renderWithProviders(<RouterProvider router={router} />, language, { session: SYNTHETIC_ADMIN });
}

describe('route behaviour', () => {
  beforeEach(() => {
    server.use(
      mock.get('/api/system/info', () => HttpResponse.json({ version: '1.4.0', commit: 'abc1234' })),
    );
  });

  it('moves focus to the main content after client-side navigation', async () => {
    const user = userEvent.setup();
    const router = createMemoryRouter(appRoutes, { initialEntries: ['/missing'] });
    await renderSignedIn(router);
    expect(screen.getByRole('main')).not.toHaveFocus();

    await user.click(screen.getByRole('link', { name: 'Volver al inicio' }));

    await screen.findByRole('heading', { level: 1, name: 'Inicio' });
    expect(screen.getByRole('main')).toHaveFocus();
  });

  it('does not steal focus on the first page load', async () => {
    const router = createMemoryRouter(appRoutes, { initialEntries: ['/'] });
    await renderSignedIn(router);

    expect(screen.getByRole('main')).not.toHaveFocus();
  });

  it('shows a translated error page even when the shell itself fails', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const router = createMemoryRouter(routesWithFailingShell(), { initialEntries: ['/'] });
    const { container } = await renderSignedIn(router, 'en');

    const main = screen.getByRole('main');
    expect(within(main).getByRole('heading', { level: 1, name: 'Something went wrong' })).toBeInTheDocument();
    expect(container).not.toHaveTextContent('render failure sentinel');
  });

  it('shows a translated error page inside the shell when a page fails to render', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const router = createMemoryRouter(routesWithFailingPage(), { initialEntries: ['/boom'] });
    const { container } = await renderSignedIn(router, 'en');

    const main = screen.getByRole('main');
    expect(within(main).getByRole('heading', { level: 1, name: 'Something went wrong' })).toBeInTheDocument();
    expect(within(main).getByRole('link', { name: 'Back to the start page' })).toHaveAttribute('href', '/');
    expect(screen.getByRole('banner')).toBeInTheDocument();
    expect(container).not.toHaveTextContent('render failure sentinel');
    expect(await axeViolations(container)).toEqual([]);
  });
});
