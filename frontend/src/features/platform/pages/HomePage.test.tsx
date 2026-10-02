import { screen, within } from '@testing-library/react';
import { http as mock, HttpResponse } from 'msw';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { beforeEach, describe, expect, it } from 'vitest';
import type { AccountResponse } from '@/api/generated/model';
import { appRoutes } from '@/app/routes';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';

function renderHome(session: AccountResponse) {
  const router = createMemoryRouter(appRoutes, { initialEntries: ['/'] });
  return renderWithProviders(<RouterProvider router={router} />, 'es-ES', { session });
}

/** The shortcuts of the start page, by name. */
function shortcuts(): string[] {
  const section = screen.getByRole('region', { name: 'Qué puedes hacer' });
  return within(section)
    .getAllByRole('link')
    .map((link) => link.getAttribute('href') ?? '');
}

describe('HomePage (interim start page until #7)', () => {
  beforeEach(() => {
    server.use(mock.get('/api/system/info', () => HttpResponse.json({ version: '1.4.0', commit: 'abc' })));
  });

  it('offers an Admin a shortcut to every section, with what is done there', async () => {
    await renderHome(SYNTHETIC_ADMIN);

    expect(shortcuts()).toEqual(['/arquebusiers', '/comparsas', '/weapon-models', '/users']);
    const section = screen.getByRole('region', { name: 'Qué puedes hacer' });
    expect(within(section).getByRole('link', { name: 'Arcabuceros' })).toHaveAccessibleDescription(
      'Altas, licencias, cursos, fotos y armas propias de los arcabuceros.',
    );
  });

  it('offers a FiringChief only the sections of their role', async () => {
    await renderHome(SYNTHETIC_FIRING_CHIEF);

    expect(shortcuts()).toEqual(['/arquebusiers', '/comparsas']);
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderHome(SYNTHETIC_ADMIN);

    expect(await axeViolations(container)).toEqual([]);
  });
});
