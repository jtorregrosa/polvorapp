import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Pencil, Trash2 } from 'lucide-react';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { Button } from './Button';
import { DescriptionList } from './DescriptionList';
import { KeyFacts } from './KeyFacts';
import { RecordHeader } from './RecordHeader';
import { SectionCard } from './SectionCard';
import { SectionGrid } from './SectionGrid';
import { Tabs } from './Tabs';
import { StatusBadge } from './StatusBadge';

// Spec: Page templates (detail), Detail pages in read mode, Action hierarchy. Synthetic data only.

describe('RecordHeader', () => {
  function Header({ onDelete = vi.fn(), onStatus = vi.fn() }) {
    return (
      <RecordHeader
        media={<span data-testid="photo">Foto</span>}
        context="Comparsa Sintética Norte · Moros"
        name="Ana Sintética Pérez"
        statuses={<StatusBadge kind="license" value="VALID" />}
        actions={<Button variant="secondary">Trasladar</Button>}
        moreActions={[
          { id: 'status', label: 'Pasar a reserva', icon: Pencil, onSelect: onStatus },
          { id: 'delete', label: 'Borrar arcabucero', icon: Trash2, onSelect: onDelete, destructive: true },
        ]}
      />
    );
  }

  it('shows the photo, the context line, the name as the page title and the statuses', async () => {
    await renderWithProviders(<Header />);

    const title = screen.getByRole('heading', { level: 1, name: 'Ana Sintética Pérez' });
    expect(title).toHaveClass('font-display', 'text-record');
    expect(screen.getByTestId('photo')).toBeInTheDocument();
    expect(screen.getByText('Comparsa Sintética Norte · Moros')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Trasladar' })).toBeInTheDocument();
  });

  it('groups rare and destructive actions in "More actions", the destructive one set apart', async () => {
    const user = userEvent.setup();
    const onDelete = vi.fn();
    await renderWithProviders(<Header onDelete={onDelete} />);

    const more = screen.getByRole('button', { name: 'Más acciones' });
    await user.click(more);
    const menu = await screen.findByRole('menu');
    const items = within(menu).getAllByRole('menuitem');
    expect(items.map((item) => item.textContent)).toEqual(['Pasar a reserva', 'Borrar arcabucero']);
    expect(items[1]).toHaveAttribute('data-variant', 'destructive');
    expect(within(menu).getByRole('separator')).toBeInTheDocument();

    await user.click(within(menu).getByRole('menuitem', { name: 'Borrar arcabucero' }));
    expect(onDelete).toHaveBeenCalledOnce();
  });

  it('closes "More actions" with Escape and returns focus to it', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Header />);
    const more = screen.getByRole('button', { name: 'Más acciones' });

    await user.click(more);
    await screen.findByRole('menu');
    await user.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    });
    expect(more).toHaveFocus();
  });
});

describe('KeyFacts', () => {
  it('lists each fact as a term and value, with a meter that has an accessible label', async () => {
    await renderWithProviders(
      <KeyFacts
        label="Datos clave"
        items={[
          {
            id: 'license',
            label: 'Licencia caduca',
            value: '12/03/2027',
            meter: { value: 0.75, label: 'Ha pasado el 75 % de la vigencia' },
          },
          { id: 'weapons', label: 'Armas propias', value: '2' },
        ]}
      />,
    );

    const facts = screen.getByRole('list', { name: 'Datos clave' });
    expect(within(facts).getAllByRole('listitem')).toHaveLength(2);
    expect(within(facts).getByText('Licencia caduca')).toBeInTheDocument();
    const meter = screen.getByRole('meter', { name: 'Ha pasado el 75 % de la vigencia' });
    expect(meter).toHaveProperty('value', 75);
    expect(meter).toHaveAttribute('min', '0');
    expect(meter).toHaveAttribute('max', '100');
  });
});

describe('Tabs', () => {
  function DetailTabs() {
    return (
      <Tabs
        label="Secciones del arcabucero"
        tabs={[
          { id: 'data', label: 'Datos', content: <p>Contenido de datos</p> },
          { id: 'weapons', label: 'Armas', count: 2, content: <p>Contenido de armas</p> },
        ]}
      />
    );
  }

  it('moves between tabs with the arrow keys and labels each panel by its tab', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<DetailTabs />);

    const list = screen.getByRole('tablist', { name: 'Secciones del arcabucero' });
    const [data, weapons] = within(list).getAllByRole('tab');
    expect(data).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tabpanel', { name: 'Datos' })).toHaveTextContent('Contenido de datos');

    if (!data) throw new Error('No first tab');
    await user.click(data);
    await user.keyboard('{ArrowRight}');

    expect(weapons).toHaveFocus();
    expect(weapons).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tabpanel', { name: /Armas/ })).toHaveTextContent('Contenido de armas');
  });

  it('marks the chosen tab with a bar and weight, not colour alone', async () => {
    await renderWithProviders(<DetailTabs />);

    expect(screen.getByRole('tab', { name: 'Datos' })).toHaveClass(
      'data-[state=active]:font-semibold',
      'data-[state=active]:after:bg-primary',
    );
  });

  it('shows a count in a tab label', async () => {
    await renderWithProviders(<DetailTabs />);

    expect(screen.getByRole('tab', { name: 'Armas (2)' })).toBeInTheDocument();
  });
});

describe('SectionGrid, SectionCard and DescriptionList', () => {
  function Sections() {
    return (
      <SectionGrid>
        <SectionCard
          title="Datos personales"
          description="Como figuran en el DNI"
          action={<Button variant="secondary">Editar</Button>}
        >
          <DescriptionList
            items={[
              { term: 'Nombre', value: 'Ana' },
              { term: 'Teléfono', value: '' },
              { term: 'Correo', value: null },
              { term: 'Fecha de nacimiento', value: '01/05/1990', mono: false },
              { term: 'DNI/NIE', value: '00000001R', mono: true },
            ]}
          />
        </SectionCard>
        <SectionCard title="Armas propias" span="full">
          <p>Lista</p>
        </SectionCard>
      </SectionGrid>
    );
  }

  it('shows each section as a titled region with its action', async () => {
    await renderWithProviders(<Sections />);

    const personal = screen.getByRole('region', { name: 'Datos personales' });
    expect(within(personal).getByRole('heading', { level: 2, name: 'Datos personales' })).toBeInTheDocument();
    expect(within(personal).getByRole('button', { name: 'Editar' })).toBeInTheDocument();
    expect(personal).toHaveTextContent('Como figuran en el DNI');
  });

  it('shows "Not given" for empty values', async () => {
    await renderWithProviders(<Sections />);

    const definitions = screen.getAllByRole('definition').map((definition) => definition.textContent);
    expect(definitions).toEqual(['Ana', 'No consta', 'No consta', '01/05/1990', '00000001R']);
    expect(screen.getByText('00000001R')).toHaveClass('font-mono');
  });

  it('adds columns on wide screens and lets a section span the whole row', async () => {
    await renderWithProviders(<Sections />);

    const grid = screen.getByRole('region', { name: 'Datos personales' }).parentElement;
    expect(grid).toHaveClass('grid', 'lg:grid-cols-2', 'wide:grid-cols-3');
    expect(screen.getByRole('region', { name: 'Armas propias' })).toHaveClass('col-span-full');
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderWithProviders(<Sections />);

    expect(await axeViolations(container)).toEqual([]);
  });
});
