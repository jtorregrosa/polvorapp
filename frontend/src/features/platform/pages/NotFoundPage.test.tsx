import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { beforeEach, describe, expect, it } from 'vitest';
import { appRoutes } from '@/app/routes';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN } from '@/test/identity';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';

function renderAt(path: string, language = 'es-ES') {
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] });
  return renderWithProviders(<RouterProvider router={router} />, language, { session: SYNTHETIC_ADMIN });
}

describe('NotFoundPage', () => {
  beforeEach(() => {
    server.use(
      mock.get('/api/system/info', () => HttpResponse.json({ version: '1.4.0', commit: 'abc1234' })),
    );
  });

  it('is shown inside the shell for an unknown route', async () => {
    await renderAt('/does/not/exist', 'en');

    const main = screen.getByRole('main');
    expect(within(main).getByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument();
    expect(screen.getByRole('banner')).toBeInTheDocument();
    expect(document.title).toBe('Page not found · PolvorApp');
  });

  it('links back to the start page', async () => {
    const user = userEvent.setup();
    await renderAt('/does/not/exist');

    await user.click(screen.getByRole('link', { name: 'Volver al inicio' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Inicio' })).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderAt('/does/not/exist');
    await screen.findByText('Versión 1.4.0');

    expect(await axeViolations(container)).toEqual([]);
  });
});
