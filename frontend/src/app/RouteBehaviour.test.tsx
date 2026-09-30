import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { createMemoryRouter, RouterProvider, type RouteObject } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { appRoutes } from './routes';

function Boom(): never {
  throw new Error('render failure sentinel');
}

/** The real route table with an extra page that throws while rendering. */
function routesWithFailingPage(): RouteObject[] {
  const [root] = appRoutes;
  const [pages] = root?.children ?? [];
  if (!root || !pages || pages.index) throw new Error('Unexpected route table shape');
  const failingPages: RouteObject = {
    ...pages,
    children: [...(pages.children ?? []), { path: 'boom', Component: Boom }],
  };
  return [{ ...root, index: false, children: [failingPages] }];
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
    await renderWithProviders(<RouterProvider router={router} />);
    expect(screen.getByRole('main')).not.toHaveFocus();

    await user.click(screen.getByRole('link', { name: 'Volver al inicio' }));

    await screen.findByRole('heading', { level: 1, name: 'Bienvenida' });
    expect(screen.getByRole('main')).toHaveFocus();
  });

  it('does not steal focus on the first page load', async () => {
    const router = createMemoryRouter(appRoutes, { initialEntries: ['/'] });
    await renderWithProviders(<RouterProvider router={router} />);

    expect(screen.getByRole('main')).not.toHaveFocus();
  });

  it('shows a translated error page even when the shell itself fails', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const [root] = appRoutes;
    if (!root) throw new Error('Unexpected route table shape');
    const router = createMemoryRouter([{ ...root, index: false, Component: Boom }], {
      initialEntries: ['/'],
    });
    const { container } = await renderWithProviders(<RouterProvider router={router} />, 'en');

    const main = screen.getByRole('main');
    expect(within(main).getByRole('heading', { level: 1, name: 'Something went wrong' })).toBeInTheDocument();
    expect(container).not.toHaveTextContent('render failure sentinel');
  });

  it('shows a translated error page inside the shell when a page fails to render', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const router = createMemoryRouter(routesWithFailingPage(), { initialEntries: ['/boom'] });
    const { container } = await renderWithProviders(<RouterProvider router={router} />, 'en');

    const main = screen.getByRole('main');
    expect(within(main).getByRole('heading', { level: 1, name: 'Something went wrong' })).toBeInTheDocument();
    expect(within(main).getByRole('link', { name: 'Back to the start page' })).toHaveAttribute('href', '/');
    expect(screen.getByRole('banner')).toBeInTheDocument();
    expect(container).not.toHaveTextContent('render failure sentinel');
    expect(await axeViolations(container)).toEqual([]);
  });
});
