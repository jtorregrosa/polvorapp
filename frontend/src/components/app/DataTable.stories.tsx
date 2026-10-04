import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { Link } from 'react-router';
import { DataTable, type DataTableColumn, type RowSelection } from './DataTable';
import { StatusBadge } from './StatusBadge';

// Synthetic data only.
interface Row {
  id: string;
  surname: string;
  comparsa: string;
  license: string;
  kilos: number;
}

const LICENSES = ['VALID', 'EXPIRING', 'EXPIRED', 'PENDING'];

const rows: Row[] = Array.from({ length: 27 }, (_, i) => ({
  id: `a${String(i + 1)}`,
  surname: `Apellido ${String(i + 1).padStart(2, '0')}`,
  comparsa: `Comparsa ${String.fromCharCode(65 + (i % 5))}`,
  license: LICENSES[i % LICENSES.length] ?? 'VALID',
  kilos: (i % 4) + 1,
}));

const columns: DataTableColumn<Row>[] = [
  { id: 'surname', header: 'Apellidos', cell: (row) => row.surname, sortValue: (row) => row.surname },
  { id: 'comparsa', header: 'Comparsa', cell: (row) => row.comparsa, sortValue: (row) => row.comparsa },
  {
    id: 'license',
    header: 'Licencia',
    cell: (row) => <StatusBadge kind="license" value={row.license} />,
  },
  {
    id: 'kilos',
    header: 'Kg',
    cell: (row) => String(row.kilos),
    sortValue: (row) => row.kilos,
    align: 'end',
  },
];

const meta = {
  title: 'Composites/DataTable',
  component: DataTable<Row>,
  args: { caption: 'Arcabuceros', data: rows, columns, getRowId: (row: Row) => row.id, pageSize: 10 },
} satisfies Meta<typeof DataTable<Row>>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Loading: Story = { args: { isLoading: true } };

export const Empty: Story = { args: { data: [] } };

/** A short list inside a page section: every row, no pagination, its own empty text. */
export const ShortList: Story = { args: { data: rows.slice(0, 3), paginated: false } };

export const ShortListEmpty: Story = {
  args: { data: [], paginated: false, emptyText: 'Todavía no hay ningún jefe de disparo asignado.' },
};

export const LongValencian: Story = {
  args: {
    caption: 'Arcabussers de la comparsa amb llicència',
    columns: [
      { ...columns[0], header: 'Cognoms de l’arcabusser' } as DataTableColumn<Row>,
      ...columns.slice(1),
    ],
  },
};

/** Many columns: on a narrow screen the table scrolls inside its region, not the page. */
export const Wide: Story = {
  args: {
    columns: [
      ...columns,
      ...['Arma', 'Frasco', 'Licencia hasta', 'Curso', 'Préstamo', 'Observaciones'].map(
        (header, index): DataTableColumn<Row> => ({
          id: `extra${String(index)}`,
          header,
          cell: (row) => `${header} ${row.id}`,
        }),
      ),
    ],
  },
};

const linkedColumns: DataTableColumn<Row>[] = [
  {
    id: 'surname',
    header: 'Apellidos',
    cell: (row) => (
      <Link to={`/arquebusiers/${row.id}`} className="font-semibold text-foreground hover:underline">
        {row.surname}
      </Link>
    ),
    secondary: (row) => (
      <span className="font-mono text-id">{`0000${row.id.slice(1).padStart(4, '0')}X`}</span>
    ),
    sortValue: (row) => row.surname,
  },
  ...columns.slice(1),
];

/**
 * Rows that lead to a record: the name is the link and the only tab stop; a click anywhere on the
 * row opens the record too. Below 768 px each row is a stacked item instead (`mobileRow`).
 */
export const RowsLeadToRecords: Story = {
  args: {
    columns: linkedColumns,
    getRowHref: (row: Row) => `/arquebusiers/${row.id}`,
    mobileRow: (row: Row) => (
      <>
        <Link to={`/arquebusiers/${row.id}`} className="font-semibold text-foreground">
          {row.surname}
        </Link>
        <span className="text-help text-muted-foreground">{row.comparsa}</span>
        <StatusBadge kind="license" value={row.license} />
      </>
    ),
  },
};

function SelectableTable() {
  const [selection, setSelection] = useState<RowSelection>({ a2: true, a14: true });
  return (
    <DataTable
      caption="Arcabuceros"
      data={rows}
      columns={linkedColumns}
      getRowId={(row) => row.id}
      getRowHref={(row) => `/arquebusiers/${row.id}`}
      rowSelection={selection}
      onRowSelectionChange={setSelection}
      getRowLabel={(row) => row.surname}
      mobileRow={(row) => (
        <Link to={`/arquebusiers/${row.id}`} className="font-semibold text-foreground">
          {row.surname}
        </Link>
      )}
    />
  );
}

/**
 * Row selection, owned by the screen: a labelled checkbox per row (and per stacked item on phones),
 * the header one for the current page (mixed when partial). The selection survives paging, sorting
 * and filtering; the count is announced. A checkbox never opens the record.
 */
export const RowSelectionStory: Story = {
  name: 'Row selection',
  render: () => <SelectableTable />,
};
