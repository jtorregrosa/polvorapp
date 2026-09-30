import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { DataTable, type DataTableColumn } from './DataTable';

// Synthetic data only.
interface Arquebusier {
  id: string;
  surname: string;
  kilos: number;
}

const people: Arquebusier[] = Array.from({ length: 23 }, (_, i) => ({
  id: `a${String(i + 1)}`,
  surname: `Apellido ${String.fromCharCode(90 - (i % 26))}${String(i + 1).padStart(2, '0')}`,
  kilos: i % 3,
}));

const columns: DataTableColumn<Arquebusier>[] = [
  { id: 'surname', header: 'Apellidos', cell: (row) => row.surname, sortValue: (row) => row.surname },
  { id: 'kilos', header: 'Kg', cell: (row) => String(row.kilos), align: 'end' },
];

function bodyRows(): HTMLElement[] {
  const [, body] = screen.getAllByRole('rowgroup');
  if (!body) throw new Error('Table has no body');
  return within(body).getAllByRole('row');
}

describe('DataTable', () => {
  afterEach(() => {
    document.documentElement.classList.remove('dark');
  });

  it('shows the first page with a translated summary', async () => {
    await renderWithProviders(
      <DataTable
        caption="Arcabuceros"
        data={people}
        columns={columns}
        getRowId={(r) => r.id}
        pageSize={10}
      />,
    );

    expect(screen.getByRole('table', { name: 'Arcabuceros' })).toBeInTheDocument();
    expect(bodyRows()).toHaveLength(10);
    expect(screen.getByText('1–10 de 23')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Anterior' })).toBeDisabled();
  });

  it('moves between pages', async () => {
    const user = userEvent.setup();
    await renderWithProviders(
      <DataTable
        caption="Arcabuceros"
        data={people}
        columns={columns}
        getRowId={(r) => r.id}
        pageSize={10}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    await user.click(screen.getByRole('button', { name: 'Siguiente' }));

    expect(
      within(screen.getByRole('navigation', { name: /^Paginación/ })).getByText('21–23 de 23'),
    ).toBeInTheDocument();
    expect(bodyRows()).toHaveLength(3);
    expect(screen.getByRole('button', { name: 'Siguiente' })).toBeDisabled();
  });

  it('changes the page size', async () => {
    const user = userEvent.setup();
    await renderWithProviders(
      <DataTable
        caption="Arcabuceros"
        data={people}
        columns={columns}
        getRowId={(r) => r.id}
        pageSize={10}
      />,
    );

    await user.selectOptions(screen.getByRole('combobox', { name: 'Filas por página' }), '50');

    expect(bodyRows()).toHaveLength(23);
  });

  it('sorts by a sortable column and exposes the direction', async () => {
    const user = userEvent.setup();
    await renderWithProviders(
      <DataTable
        caption="Arcabuceros"
        data={people}
        columns={columns}
        getRowId={(r) => r.id}
        pageSize={50}
      />,
    );
    const header = screen.getByRole('columnheader', { name: /^Apellidos/ });
    expect(header).toHaveAttribute('aria-sort', 'none');
    expect(screen.getByRole('columnheader', { name: 'Kg' })).not.toHaveAttribute('aria-sort');

    await user.click(within(header).getByRole('button'));
    expect(header).toHaveAttribute('aria-sort', 'ascending');
    const ascending = bodyRows().map((row) => row.textContent);
    expect(ascending).toEqual([...ascending].sort());

    await user.click(within(header).getByRole('button'));
    expect(header).toHaveAttribute('aria-sort', 'descending');
    expect(bodyRows()[0]?.textContent).toBe(ascending.at(-1));
  });

  it('announces the sort state on the button and sort changes in a live region', async () => {
    const user = userEvent.setup();
    await renderWithProviders(
      <DataTable
        caption="Arcabuceros"
        data={people}
        columns={columns}
        getRowId={(r) => r.id}
        pageSize={10}
      />,
    );

    const button = screen.getByRole('button', { name: 'Apellidos, sin ordenar' });
    await user.click(button);

    expect(screen.getByRole('button', { name: 'Apellidos, orden ascendente' })).toBe(button);
    expect(screen.getByRole('status')).toHaveTextContent('Ordenado por Apellidos: orden ascendente');
  });

  it('announces page changes in the live region', async () => {
    const user = userEvent.setup();
    await renderWithProviders(
      <DataTable
        caption="Arcabuceros"
        data={people}
        columns={columns}
        getRowId={(r) => r.id}
        pageSize={10}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Siguiente' }));

    expect(screen.getByRole('status')).toHaveTextContent('11–20 de 23');
  });

  it('sorts text with the rules of the active language', async () => {
    const user = userEvent.setup();
    const names = ['Zapata', 'Álvarez', 'Martínez', 'Ñúñez', 'Navarro'].map((surname, i) => ({
      id: `n${String(i)}`,
      surname,
      kilos: 1,
    }));
    await renderWithProviders(
      <DataTable caption="Arcabuceros" data={names} columns={columns} getRowId={(r) => r.id} />,
    );

    await user.click(screen.getByRole('button', { name: /^Apellidos/ }));

    expect(bodyRows().map((row) => row.firstChild?.textContent)).toEqual([
      'Álvarez',
      'Martínez',
      'Navarro',
      'Ñúñez',
      'Zapata',
    ]);
  });

  it('returns to a valid page when the data shrinks', async () => {
    const user = userEvent.setup();
    const { rerender } = await renderWithProviders(
      <DataTable
        caption="Arcabuceros"
        data={people}
        columns={columns}
        getRowId={(r) => r.id}
        pageSize={10}
      />,
    );
    await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    await user.click(screen.getByRole('button', { name: 'Siguiente' }));

    const fewer = people.slice(0, 4);
    rerender(
      <DataTable caption="Arcabuceros" data={fewer} columns={columns} getRowId={(r) => r.id} pageSize={10} />,
    );

    expect(bodyRows()).toHaveLength(4);
    expect(screen.getByText('1–4 de 4')).toBeInTheDocument();
  });

  it('names its pagination after the table', async () => {
    await renderWithProviders(
      <DataTable caption="Arcabuceros" data={people} columns={columns} getRowId={(r) => r.id} />,
    );

    expect(screen.getByRole('navigation', { name: 'Paginación de Arcabuceros' })).toBeInTheDocument();
  });

  it('shows every row without pagination when it is a short list', async () => {
    await renderWithProviders(
      <DataTable
        caption="Jefes de disparo"
        data={people}
        columns={columns}
        getRowId={(r) => r.id}
        pageSize={10}
        paginated={false}
      />,
    );

    expect(bodyRows()).toHaveLength(people.length);
    expect(
      screen.queryByRole('navigation', { name: 'Paginación de Jefes de disparo' }),
    ).not.toBeInTheDocument();
  });

  it('can keep a column header for screen readers only', async () => {
    await renderWithProviders(
      <DataTable
        caption="Jefes de disparo"
        data={people}
        columns={[...columns, { id: 'actions', header: 'Acciones', hideHeader: true, cell: () => 'x' }]}
        getRowId={(r) => r.id}
      />,
    );

    const header = screen.getByRole('columnheader', { name: 'Acciones' });
    expect(within(header).getByText('Acciones')).toHaveClass('sr-only');
  });

  it('shows its own empty text when given one', async () => {
    await renderWithProviders(
      <DataTable
        caption="Jefes de disparo"
        data={[]}
        columns={columns}
        getRowId={(r) => r.id}
        emptyText="Todavía no hay ningún jefe de disparo asignado."
      />,
    );

    expect(screen.getByText('Todavía no hay ningún jefe de disparo asignado.')).toBeInTheDocument();
  });

  it('shows a translated empty state', async () => {
    await renderWithProviders(
      <DataTable caption="Arcabuceros" data={[]} columns={columns} getRowId={(r) => r.id} />,
      'en',
    );

    expect(screen.getByText('No results')).toBeInTheDocument();
    expect(screen.queryByText(/of 0/)).not.toBeInTheDocument();
  });

  it('shows a loading state', async () => {
    await renderWithProviders(
      <DataTable caption="Arcabuceros" data={[]} columns={columns} getRowId={(r) => r.id} isLoading />,
    );

    expect(screen.getByRole('table')).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('status')).toHaveTextContent('Cargando…');
    expect(screen.queryByText('Sin resultados')).not.toBeInTheDocument();
  });

  it('scrolls horizontally inside a focusable, named region', async () => {
    await renderWithProviders(
      <DataTable caption="Arcabuceros" data={people} columns={columns} getRowId={(r) => r.id} />,
    );

    const region = screen.getByRole('region', { name: 'Arcabuceros' });
    expect(region).toHaveAttribute('tabindex', '0');
    expect(region).toHaveClass('overflow-x-auto');
    // The focusable region is the only scroll container, so the keyboard can scroll the table.
    expect(region.querySelector('[class*="overflow-x-auto"]')).toBeNull();
    expect(region.querySelector(':scope > table')).not.toBeNull();
  });

  it.each([false, true])('has no accessibility violations (dark=%s)', async (dark) => {
    document.documentElement.classList.toggle('dark', dark);
    const { container } = await renderWithProviders(
      <DataTable
        caption="Arcabuceros"
        data={people}
        columns={columns}
        getRowId={(r) => r.id}
        pageSize={10}
      />,
    );

    expect(await axeViolations(container)).toEqual([]);
  });
});
