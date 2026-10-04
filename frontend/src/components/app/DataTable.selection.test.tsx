import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { createMemoryRouter, Link, RouterProvider } from 'react-router';
import { DataTable, type DataTableColumn, type RowSelection } from './DataTable';

// Synthetic data only.
interface Person {
  id: string;
  name: string;
}

const PEOPLE: Person[] = Array.from({ length: 23 }, (_, i) => ({
  id: `p${String(i + 1)}`,
  name: `Sintética ${String(i + 1).padStart(2, '0')}`,
}));

const columns: DataTableColumn<Person>[] = [
  {
    id: 'name',
    header: 'Nombre',
    cell: (row) => <Link to={`/people/${row.id}`}>{row.name}</Link>,
    sortValue: (row) => row.name,
    rowHeader: true,
  },
];

let latest: RowSelection = {};

function Selectable({ data = PEOPLE, initial = {} }: { data?: Person[]; initial?: RowSelection }) {
  const [selection, setSelection] = useState<RowSelection>(initial);
  const [filtered, setFiltered] = useState(false);
  return (
    <>
      <button
        type="button"
        onClick={() => {
          setFiltered((value) => !value);
        }}
      >
        Filtrar
      </button>
      <DataTable
        caption="Personas"
        data={filtered ? data.slice(0, 3) : data}
        columns={columns}
        getRowId={(row) => row.id}
        getRowHref={(row) => `/people/${row.id}`}
        pageSize={10}
        rowSelection={selection}
        onRowSelectionChange={(next) => {
          latest = next;
          setSelection(next);
        }}
        getRowLabel={(row) => row.name}
        mobileRow={(row) => <Link to={`/people/${row.id}`}>{row.name}</Link>}
      />
    </>
  );
}

function renderSelectable(props: { data?: Person[]; initial?: RowSelection } = {}, width = 1024) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: width });
  const router = createMemoryRouter(
    [
      { path: '/people', element: <Selectable {...props} /> },
      { path: '/people/:id', element: <h1>Ficha</h1> },
    ],
    { initialEntries: ['/people'] },
  );
  return renderWithProviders(<RouterProvider router={router} />);
}

const selected = () => Object.keys(latest).filter((id) => latest[id]);

describe('DataTable row selection', () => {
  afterEach(() => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 });
    latest = {};
  });

  it('labels a checkbox per row with its name and selects without opening the record', async () => {
    const user = userEvent.setup();
    await renderSelectable();

    await user.click(screen.getByRole('checkbox', { name: 'Seleccionar Sintética 01' }));

    expect(selected()).toEqual(['p1']);
    expect(screen.queryByRole('heading', { name: 'Ficha' })).not.toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Seleccionar Sintética 01' })).toBeChecked();
    expect(screen.getByRole('status')).toHaveTextContent('1 seleccionado');
    expect(screen.getByRole('rowheader', { name: /Sintética 01/ }).closest('tr')).toHaveAttribute(
      'data-state',
      'selected',
    );
  });

  it('selects and clears the current page only, shown as mixed when partial', async () => {
    const user = userEvent.setup();
    await renderSelectable({ initial: { p15: true } });
    const page = screen.getByRole('checkbox', { name: 'Seleccionar todos los de esta página' });

    await user.click(screen.getByRole('checkbox', { name: 'Seleccionar Sintética 02' }));
    expect(page).toHaveAttribute('aria-checked', 'mixed');

    await user.click(page);
    expect(selected().sort()).toEqual(
      ['p1', 'p10', 'p15', 'p2', 'p3', 'p4', 'p5', 'p6', 'p7', 'p8', 'p9'].sort(),
    );
    expect(page).toBeChecked();
    expect(screen.getByRole('status')).toHaveTextContent('11 seleccionados');

    await user.click(page);
    expect(selected()).toEqual(['p15']);
  });

  it('keeps the selection across pages and sorting', async () => {
    const user = userEvent.setup();
    await renderSelectable();

    await user.click(screen.getByRole('checkbox', { name: 'Seleccionar Sintética 01' }));
    await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    await user.click(screen.getByRole('checkbox', { name: 'Seleccionar Sintética 12' }));
    await user.click(screen.getByRole('button', { name: 'Anterior' }));
    await user.click(screen.getByRole('button', { name: /Nombre/ }));

    expect(selected().sort()).toEqual(['p1', 'p12']);
    expect(screen.getByRole('checkbox', { name: 'Seleccionar Sintética 01' })).toBeChecked();
  });

  it('keeps a selected row that a filter hides', async () => {
    const user = userEvent.setup();
    await renderSelectable({ data: PEOPLE.slice(0, 5) });
    await user.click(screen.getByRole('checkbox', { name: 'Seleccionar Sintética 05' }));

    await user.click(screen.getByRole('button', { name: 'Filtrar' }));
    expect(screen.queryByRole('checkbox', { name: 'Seleccionar Sintética 05' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Filtrar' }));

    expect(selected()).toEqual(['p5']);
    expect(screen.getByRole('checkbox', { name: 'Seleccionar Sintética 05' })).toBeChecked();
  });

  it('gives each stacked item on a phone its labelled checkbox', async () => {
    const user = userEvent.setup();
    const { container } = await renderSelectable({ data: PEOPLE.slice(0, 3) }, 360);

    const items = within(screen.getByRole('list', { name: 'Personas' })).getAllByRole('listitem');
    expect(items).toHaveLength(3);
    const first = items[0];
    if (!first) throw new Error('No first item');
    await user.click(within(first).getByRole('checkbox', { name: 'Seleccionar Sintética 01' }));

    expect(selected()).toEqual(['p1']);
    expect(screen.queryByRole('heading', { name: 'Ficha' })).not.toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('still opens the record from the rest of the row, but not from the selection cell', async () => {
    const user = userEvent.setup();
    await renderSelectable();

    const box = screen.getByRole('checkbox', { name: 'Seleccionar Sintética 01' });
    const cell = box.closest('td');
    if (!cell) throw new Error('No selection cell');
    await user.click(cell);
    expect(screen.queryByRole('heading', { name: 'Ficha' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('rowheader', { name: /Sintética 02/ }));
    expect(await screen.findByRole('heading', { name: 'Ficha' })).toBeInTheDocument();
  });

  it('toggles a row with the keyboard', async () => {
    const user = userEvent.setup();
    await renderSelectable();

    screen.getByRole('checkbox', { name: 'Seleccionar Sintética 03' }).focus();
    await user.keyboard(' ');
    expect(selected()).toEqual(['p3']);
    await user.keyboard(' ');
    expect(selected()).toEqual([]);
  });

  it('selects only the rows of the page shown', async () => {
    const user = userEvent.setup();
    await renderSelectable();

    await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    await user.click(screen.getByRole('checkbox', { name: 'Seleccionar todos los de esta página' }));

    expect(selected().sort()).toEqual(
      ['p11', 'p12', 'p13', 'p14', 'p15', 'p16', 'p17', 'p18', 'p19', 'p20'].sort(),
    );
  });

  it('offers the page selection on a phone too', async () => {
    const user = userEvent.setup();
    await renderSelectable({ data: PEOPLE.slice(0, 3), initial: { p2: true } }, 360);

    const page = screen.getByRole('checkbox', { name: 'Seleccionar todos los de esta página' });
    expect(page).toHaveAttribute('aria-checked', 'mixed');
    await user.click(page);

    expect(selected().sort()).toEqual(['p1', 'p2', 'p3']);
    expect(screen.getByRole('status')).toHaveTextContent('3 seleccionados');
  });

  it('has no accessibility violations with rows selected', async () => {
    const { container } = await renderSelectable({ initial: { p1: true } });

    expect(await axeViolations(container)).toEqual([]);
  });

  it('shows no checkbox without selection', async () => {
    await renderWithProviders(
      <DataTable
        caption="Personas"
        data={PEOPLE.slice(0, 2)}
        columns={[{ id: 'name', header: 'Nombre', cell: (row) => row.name }]}
        getRowId={(row) => row.id}
      />,
    );

    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  });
});
