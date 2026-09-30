import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { appRoutes } from './routes';

function renderAt(path: string, language = 'es-ES') {
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] });
  return renderWithProviders(<RouterProvider router={router} />, language);
}

function setViewportWidth(width: number) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: width });
}

describe('AppLayout (platform: Application shell)', () => {
  beforeEach(() => {
    setViewportWidth(1280);
    server.use(
      mock.get('/api/system/info', () => HttpResponse.json({ version: '1.4.0', commit: 'abc1234' })),
    );
  });

  afterEach(() => {
    document.documentElement.classList.remove('dark');
    setViewportWidth(1024);
  });

  it('renders the sidebar with the PolvorApp mark, the navigation and the API version', async () => {
    await renderAt('/');

    const navigation = screen.getByRole('navigation', { name: 'Navegación principal' });
    expect(within(navigation).getByRole('link', { name: 'Inicio' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: 'PolvorApp' })).toHaveAttribute('href', '/');
    expect(await screen.findByText('Versión 1.4.0')).toBeInTheDocument();
  });

  it('shows a translated notice in the sidebar when the API is unavailable', async () => {
    server.use(mock.get('/api/system/info', () => new HttpResponse(null, { status: 503 })));

    await renderAt('/');

    expect(await screen.findByText('Versión no disponible')).toBeInTheDocument();
  });

  it('renders the top bar with breadcrumbs, language and theme switchers', async () => {
    await renderAt('/does/not/exist');

    const banner = screen.getByRole('banner');
    const breadcrumbs = within(banner).getByRole('navigation', { name: 'Ruta de navegación' });
    expect(within(breadcrumbs).getByRole('link', { name: 'Inicio' })).toHaveAttribute('href', '/');
    expect(within(breadcrumbs).getByText('Página no encontrada')).toHaveAttribute('aria-current', 'page');
    expect(within(banner).getByRole('combobox', { name: 'Idioma' })).toBeInTheDocument();
    expect(within(banner).getByRole('button', { name: /^Tema/ })).toBeInTheDocument();
  });

  it('keeps the skip link first and the focusable main landmark', async () => {
    await renderAt('/');

    expect(screen.getAllByRole('link')[0]).toHaveTextContent('Saltar al contenido');
    const main = screen.getByRole('main');
    expect(main).toHaveAttribute('id', 'main');
    expect(main).toHaveAttribute('tabindex', '-1');
    expect(within(main).getByRole('heading', { level: 1, name: 'Bienvenida' })).toBeInTheDocument();
  });

  it('opens the navigation in a drawer on small screens and closes it after choosing', async () => {
    setViewportWidth(360);
    const user = userEvent.setup();
    await renderAt('/does/not/exist');
    expect(screen.queryByRole('navigation', { name: 'Navegación principal' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Mostrar u ocultar la navegación' }));
    const drawer = await screen.findByRole('dialog');
    await user.click(within(drawer).getByRole('link', { name: 'Inicio' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(await screen.findByRole('heading', { level: 1, name: 'Bienvenida' })).toBeInTheDocument();
    expect(screen.getByRole('main')).toHaveFocus();
  });

  it('exposes whether the navigation is expanded and removes a collapsed sidebar from the tab order', async () => {
    const user = userEvent.setup();
    await renderAt('/');
    const trigger = screen.getByRole('button', { name: 'Mostrar u ocultar la navegación' });
    expect(trigger).toHaveAttribute('aria-expanded', 'true');

    await user.click(trigger);

    expect(trigger).toHaveAttribute('aria-expanded', 'false');
    const container = document.querySelector('[data-slot="sidebar-container"]');
    expect(container).toHaveAttribute('inert');
  });

  it('does not store the sidebar state in a cookie', async () => {
    const user = userEvent.setup();
    await renderAt('/');

    await user.click(screen.getByRole('button', { name: 'Mostrar u ocultar la navegación' }));

    expect(document.cookie).not.toContain('sidebar_state');
  });

  it('closes the navigation drawer with Escape', async () => {
    setViewportWidth(360);
    const user = userEvent.setup();
    await renderAt('/');

    await user.click(screen.getByRole('button', { name: 'Mostrar u ocultar la navegación' }));
    await screen.findByRole('dialog');
    await user.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: 'Mostrar u ocultar la navegación' })).toHaveFocus();
  });

  it('keeps the document title in the active language', async () => {
    const { i18n } = await renderAt('/');
    expect(document.title).toBe('Bienvenida · PolvorApp');

    await act(() => i18n.changeLanguage('en'));

    expect(document.title).toBe('Welcome · PolvorApp');
  });

  it.each([false, true])('has no accessibility violations (dark=%s)', async (dark) => {
    document.documentElement.classList.toggle('dark', dark);
    const { container } = await renderAt('/');
    await screen.findByText('Versión 1.4.0');

    expect(await axeViolations(container)).toEqual([]);
  });
});
