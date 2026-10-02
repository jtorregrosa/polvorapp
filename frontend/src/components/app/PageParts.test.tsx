import { screen, within } from '@testing-library/react';
import { Inbox } from 'lucide-react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { AlertBanner } from './AlertBanner';
import { EmptyState } from './EmptyState';
import { PageHeader } from './PageHeader';
import { StatCard } from './StatCard';

afterEach(() => {
  document.documentElement.classList.remove('dark');
});

describe('PageHeader', () => {
  it('renders the page title as the only h1 with description, actions and back link', async () => {
    await renderWithProviders(
      <MemoryRouter>
        <PageHeader
          title="Arcabuceros"
          description="Listado de la comparsa"
          back={{ to: '/', label: 'Volver' }}
          actions={<button type="button">Nuevo</button>}
        />
      </MemoryRouter>,
    );

    expect(screen.getByRole('heading', { level: 1, name: 'Arcabuceros' })).toBeInTheDocument();
    expect(screen.getByText('Listado de la comparsa')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Volver' })).toHaveAttribute('href', '/');
    expect(screen.getByRole('button', { name: 'Nuevo' })).toBeInTheDocument();
  });

  it('renders only the title when nothing else is given', async () => {
    const { container } = await renderWithProviders(<PageHeader title="Inicio" />);

    expect(container.querySelectorAll('a, button, p')).toHaveLength(0);
  });
});

describe('focus on mount', () => {
  it('moves focus to the page title when asked', async () => {
    await renderWithProviders(
      <MemoryRouter>
        <PageHeader title="Revisa tu correo" focusOnMount />
      </MemoryRouter>,
    );

    expect(screen.getByRole('heading', { level: 1, name: 'Revisa tu correo' })).toHaveFocus();
  });

  it('moves focus to a notice when asked, and not otherwise', async () => {
    await renderWithProviders(
      <>
        <AlertBanner severity="info">Sin foco</AlertBanner>
        <AlertBanner severity="success" focusOnMount>
          Usuario desactivado.
        </AlertBanner>
      </>,
    );

    expect(screen.getByText('Usuario desactivado.').closest('[data-severity]')).toHaveFocus();
    expect(screen.getByText('Sin foco').closest('[data-severity]')).not.toHaveAttribute('tabindex');
  });
});

describe('EmptyState', () => {
  it('shows an icon, a heading, a description and an optional action', async () => {
    await renderWithProviders(
      <EmptyState
        icon={Inbox}
        title="Sin arcabuceros"
        description="Añade el primero para empezar."
        action={<button type="button">Añadir</button>}
      />,
    );

    const heading = screen.getByRole('heading', { level: 2, name: 'Sin arcabuceros' });
    const region = heading.parentElement;
    if (!region) throw new Error('EmptyState must render a container');
    expect(within(region).getByText('Añade el primero para empezar.')).toBeInTheDocument();
    expect(within(region).getByRole('button', { name: 'Añadir' })).toBeInTheDocument();
  });

  it('uses the requested heading level', async () => {
    await renderWithProviders(<EmptyState title="Nada" headingLevel={3} />);

    expect(screen.getByRole('heading', { level: 3, name: 'Nada' })).toBeInTheDocument();
  });
});

describe('AlertBanner', () => {
  it('announces the severity even without a title', async () => {
    await renderWithProviders(<AlertBanner severity="warning">Revisa los datos</AlertBanner>);

    expect(screen.getByRole('status')).toHaveTextContent('Aviso: Revisa los datos');
  });

  it('keeps its shape in forced colours with a border (Windows high contrast)', async () => {
    await renderWithProviders(<AlertBanner severity="info">Dato</AlertBanner>);

    // A transparent border takes the system text colour when colours are forced.
    expect(screen.getByRole('status')).toHaveClass('border', 'border-transparent');
  });

  it.each([
    ['error', 'alert', 'Error'],
    ['warning', 'status', 'Aviso'],
    ['success', 'status', 'Correcto'],
    ['info', 'status', 'Información'],
  ] as const)(
    'announces %s with role %s and a translated severity',
    async (severity, role, severityLabel) => {
      await renderWithProviders(
        <AlertBanner severity={severity} title="Título">
          Detalle
        </AlertBanner>,
      );

      const banner = screen.getByRole(role);
      expect(banner).toHaveAttribute('data-severity', severity);
      expect(banner).toHaveTextContent(severityLabel);
      expect(banner).toHaveTextContent('Título');
      expect(banner).toHaveTextContent('Detalle');
      expect(banner.querySelector('svg[aria-hidden="true"]')).not.toBeNull();
    },
  );
});

describe('StatCard', () => {
  it('shows the value with its label and description', async () => {
    await renderWithProviders(
      <StatCard label="Licencias caducadas" value="12" description="En la edición 2027" />,
    );

    const card = screen.getByText('Licencias caducadas').closest('[data-stat-card]');
    expect(card).toHaveTextContent('12');
    expect(card).toHaveTextContent('En la edición 2027');
  });

  it('describes the figure with its description instead of adding it to the link name', async () => {
    await renderWithProviders(
      <MemoryRouter>
        <StatCard label="Pedidos enviados" value="5" description="De 20 comparsas" to="/orders" />
      </MemoryRouter>,
    );

    const link = screen.getByRole('link', { name: 'Pedidos enviados 5' });
    expect(link).toHaveAccessibleDescription('De 20 comparsas');
  });

  it('becomes a link when a destination is given', async () => {
    await renderWithProviders(
      <MemoryRouter>
        <StatCard label="Pedidos enviados" value="5" to="/orders" />
      </MemoryRouter>,
    );

    const link = screen.getByRole('link', { name: 'Pedidos enviados 5' });
    expect(link).toHaveAttribute('href', '/orders');
  });
});

describe('page parts accessibility', () => {
  it.each([false, true])('has no violations (dark=%s)', async (dark) => {
    document.documentElement.classList.toggle('dark', dark);
    const { container } = await renderWithProviders(
      <MemoryRouter>
        <main>
          <PageHeader title="Panel" description="Resumen" actions={<button type="button">Acción</button>} />
          <StatCard label="Arcabuceros activos" value="56" to="/" />
          <AlertBanner severity="warning" title="Licencias">
            3 caducan pronto
          </AlertBanner>
          <AlertBanner severity="error" title="Error">
            No se pudo guardar
          </AlertBanner>
          <EmptyState icon={Inbox} title="Sin datos" description="Todavía no hay nada." />
        </main>
      </MemoryRouter>,
    );

    expect(await axeViolations(container)).toEqual([]);
  });
});
