import { act, screen, within } from '@testing-library/react';
import { http as mock, HttpResponse } from 'msw';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { beforeEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { appRoutes } from './routes';

function renderAt(path: string, language = 'es-ES') {
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] });
  return renderWithProviders(<RouterProvider router={router} />, language);
}

describe('AppShell', () => {
  beforeEach(() => {
    server.use(
      mock.get('/api/system/info', () => HttpResponse.json({ version: '1.4.0', commit: 'abc1234' })),
    );
  });

  it('renders the header with the application name and the language switcher', async () => {
    await renderAt('/');

    const header = screen.getByRole('banner');
    expect(within(header).getByText('PolvorApp')).toBeInTheDocument();
    expect(within(header).getByRole('combobox', { name: 'Idioma' })).toBeInTheDocument();
  });

  it('renders the home page inside the main landmark', async () => {
    await renderAt('/');

    const main = screen.getByRole('main');
    expect(within(main).getByRole('heading', { level: 1, name: 'Bienvenida' })).toBeInTheDocument();
    expect(main).toHaveAttribute('id', 'main');
    expect(main).toHaveAttribute('tabindex', '-1');
  });

  it('offers a skip link to the main content as the first focusable element', async () => {
    await renderAt('/');

    const links = screen.getAllByRole('link');
    expect(links[0]).toHaveTextContent('Saltar al contenido');
    expect(links[0]).toHaveAttribute('href', '#main');
  });

  it('keeps the document title in the active language', async () => {
    const { i18n } = await renderAt('/');
    expect(document.title).toBe('Bienvenida · PolvorApp');

    await act(() => i18n.changeLanguage('en'));

    expect(document.title).toBe('Welcome · PolvorApp');
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderAt('/');
    await screen.findByText('Versión 1.4.0');

    expect(await axeViolations(container)).toEqual([]);
  });
});
