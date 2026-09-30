import type { Meta, StoryObj } from '@storybook/react-vite';
import { DataTable, type DataTableColumn } from './DataTable';
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

export const LongValencian: Story = {
  args: {
    caption: 'Arcabussers de la comparsa amb llicència',
    columns: [
      { ...columns[0], header: 'Cognoms de l’arcabusser' } as DataTableColumn<Row>,
      ...columns.slice(1),
    ],
  },
};
