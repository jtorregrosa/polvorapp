import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { appRoutes } from './routes';

function renderAt(path: string, language = 'es-ES', session = SYNTHETIC_ADMIN) {
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] });
  return renderWithProviders(<RouterProvider router={router} />, language, { session });
}

/** The navigation's sections: the label of each group (none for the first) and its links. */
function navigationShape() {
  const navigation = screen.getByRole('navigation', { name: 'Navegación principal' });
  return [...navigation.querySelectorAll('[data-sidebar="group"]')].map((group) => [
    group.getAttribute('role') === 'group'
      ? group.querySelector('[data-sidebar="group-label"]')?.textContent
      : null,
    within(group as HTMLElement)
      .getAllByRole('link')
      .map((link) => link.textContent),
  ]);
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

  it('groups the navigation in named sections for an Admin, in the specified order', async () => {
    await renderAt('/');

    expect(navigationShape()).toEqual([
      [null, ['Inicio']],
      ['Registro', ['Arcabuceros', 'Comparsas', 'Estadísticas']],
      ['Fiestas', ['Ediciones', 'Pedidos', 'Reparto']],
      ['Administración', ['Modelos de arma', 'Usuarios', 'Auditoría', 'Privacidad', 'Ajustes']],
    ]);
    expect(screen.getByRole('group', { name: 'Administración' })).toBeInTheDocument();
  });

  it('leaves out the administration section and its label for a FiringChief', async () => {
    await renderAt('/', 'es-ES', SYNTHETIC_FIRING_CHIEF);

    expect(navigationShape().map(([label]) => label)).toEqual([null, 'Registro', 'Fiestas']);
    expect(screen.queryByRole('group', { name: 'Administración' })).not.toBeInTheDocument();
  });

  it('shows a translated notice in the sidebar when the API is unavailable', async () => {
    server.use(mock.get('/api/system/info', () => new HttpResponse(null, { status: 503 })));

    await renderAt('/');

    expect(await screen.findByText('Versión no disponible')).toBeInTheDocument();
  });

  it('renders the top bar with the breadcrumbs and the user menu, holding the switchers', async () => {
    const user = userEvent.setup();
    await renderAt('/does/not/exist');

    const banner = screen.getByRole('banner');
    const breadcrumbs = within(banner).getByRole('navigation', { name: 'Ruta de navegación' });
    expect(within(breadcrumbs).getByRole('link', { name: 'Inicio' })).toHaveAttribute('href', '/');
    expect(within(breadcrumbs).getByText('Página no encontrada')).toHaveAttribute('aria-current', 'page');
    expect(within(banner).queryByRole('combobox', { name: 'Idioma' })).not.toBeInTheDocument();

    await user.click(within(banner).getByRole('button', { name: 'Menú de Admin Sintética' }));
    const menu = await screen.findByRole('menu');
    expect(within(menu).getByRole('group', { name: 'Idioma' })).toBeInTheDocument();
    expect(within(menu).getByRole('group', { name: 'Tema' })).toBeInTheDocument();
  });

  it('changes the theme at once from the user menu', async () => {
    const user = userEvent.setup();
    await renderAt('/');

    await user.click(screen.getByRole('button', { name: 'Menú de Admin Sintética' }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'Oscuro' }));

    expect(document.documentElement).toHaveClass('dark');
  });

  it('aligns the top bar with the content, up to 1680 px beside the sidebar', async () => {
    await renderAt('/');

    const bar = screen.getByRole('banner').firstElementChild;
    const content = screen.getByRole('main').firstElementChild;
    expect(bar).toHaveClass('max-w-page');
    expect(content).toHaveClass('max-w-page');
    expect(content).not.toHaveClass('mx-auto');
  });

  it('shows the navigation on the night surface', async () => {
    await renderAt('/');

    const sidebar = document.querySelector('[data-slot="sidebar-inner"]');
    expect(sidebar).toHaveClass('bg-sidebar');
    const navigation = screen.getByRole('navigation', { name: 'Navegación principal' });
    expect(within(navigation).getByRole('link', { name: 'Inicio' })).toHaveClass(
      'data-[active=true]:before:bg-sidebar-primary',
    );
  });

  it('keeps the skip link first and the focusable main landmark', async () => {
    await renderAt('/');

    expect(screen.getAllByRole('link')[0]).toHaveTextContent('Saltar al contenido');
    const main = screen.getByRole('main');
    expect(main).toHaveAttribute('id', 'main');
    expect(main).toHaveAttribute('tabindex', '-1');
    expect(within(main).getByRole('heading', { level: 1, name: 'Inicio' })).toBeInTheDocument();
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
    expect(await screen.findByRole('heading', { level: 1, name: 'Inicio' })).toBeInTheDocument();
    expect(screen.getByRole('main')).toHaveFocus();
  });

  it('collapses the sidebar to an icon rail that stays operable, from a trigger named after its action', async () => {
    const user = userEvent.setup();
    await renderAt('/');
    const trigger = screen.getByRole('button', { name: 'Contraer la navegación' });
    expect(trigger).not.toHaveAttribute('aria-expanded');

    await user.click(trigger);

    expect(screen.getByRole('button', { name: 'Expandir la navegación' })).toBeInTheDocument();
    const container = document.querySelector('[data-slot="sidebar-container"]');
    expect(container).not.toHaveAttribute('inert');
    expect(
      within(screen.getByRole('navigation', { name: 'Navegación principal' })).getByRole('link', {
        name: 'Pedidos',
      }),
    ).toBeInTheDocument();
  });

  it('does not store the sidebar state in a cookie', async () => {
    const user = userEvent.setup();
    await renderAt('/');

    await user.click(screen.getByRole('button', { name: 'Contraer la navegación' }));

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
    expect(document.title).toBe('Inicio · PolvorApp');

    await act(() => i18n.changeLanguage('en'));

    expect(document.title).toBe('Home · PolvorApp');
  });

  describe('FiringChief armband (platform: FiringChief armband)', () => {
    const armband = () => document.querySelector('[data-slot="sidebar-armband"]');

    it('shows the translated role at the bottom of the sidebar, and follows the language', async () => {
      const { i18n } = await renderAt('/', 'es-ES', SYNTHETIC_FIRING_CHIEF);

      expect(armband()).toHaveTextContent('Jefe de disparo');

      await act(() => i18n.changeLanguage('ca-ES-valencia'));

      expect(armband()).toHaveTextContent('Cap de disparada');
    });

    it('shows no armband to an Admin', async () => {
      await renderAt('/');

      await screen.findByText('Versión 1.4.0');
      expect(armband()).toBeNull();
    });

    it('shows the armband to a FiringChief without comparsa cards', async () => {
      server.use(mock.get('/api/comparsas', () => HttpResponse.json([])));

      await renderAt('/', 'es-ES', SYNTHETIC_FIRING_CHIEF);

      await screen.findByText('Versión 1.4.0');
      expect(screen.queryByRole('navigation', { name: 'Mis comparsas' })).not.toBeInTheDocument();
      expect(armband()).toHaveTextContent('Jefe de disparo');
    });

    it('shows the armband in the drawer on a phone, not in the bottom bar', async () => {
      setViewportWidth(360);
      const user = userEvent.setup();
      await renderAt('/', 'es-ES', SYNTHETIC_FIRING_CHIEF);

      const bar = screen.getByRole('navigation', { name: 'Accesos directos' });
      expect(within(bar).queryByText('Jefe de disparo')).toBeNull();
      await user.click(screen.getByRole('button', { name: 'Mostrar u ocultar la navegación' }));

      const drawer = await screen.findByRole('dialog');
      expect(within(drawer).getByText('Jefe de disparo')).toBeInTheDocument();
    });
  });

  it.each([false, true])('has no accessibility violations (dark=%s)', async (dark) => {
    document.documentElement.classList.toggle('dark', dark);
    const { container } = await renderAt('/');
    await screen.findByText('Versión 1.4.0');

    expect(await axeViolations(container)).toEqual([]);
  });
});
