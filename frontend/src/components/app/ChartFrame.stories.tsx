import type { Meta, StoryObj } from '@storybook/react-vite';
import { ChartFrame } from './ChartFrame';

const meta = {
  title: 'Composites/ChartFrame',
  component: ChartFrame,
  args: {
    title: 'Arcabuceros por edición',
    summary: 'Las tendencias necesitan al menos dos ediciones.',
    categoryLabel: 'Edición',
    series: [
      { key: 'active', label: 'En activo' },
      { key: 'reserve', label: 'Reserva' },
    ],
    data: [{ id: '2031', label: '2031', provisional: true, values: { active: 412, reserve: 30 } }],
  },
} satisfies Meta<typeof ChartFrame>;

export default meta;
type Story = StoryObj<typeof meta>;

/** Without a drawing (too few points): the table is shown directly. */
export const TableOnly: Story = {};
