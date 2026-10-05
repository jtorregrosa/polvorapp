import type { Meta, StoryObj } from '@storybook/react-vite';
import { LineChart } from './LineChart';

// Synthetic shares of women among active arquebusiers; the last edition is in progress.
const shares = [
  { year: '2027', women: 0.21, firstYear: 0.06 },
  { year: '2028', women: 0.22, firstYear: 0.05 },
  { year: '2029', women: 0.24, firstYear: 0.07 },
  { year: '2030', women: 0.25, firstYear: 0.06 },
  { year: '2031', women: 0.27, firstYear: 0.08 },
];

const percent = (value: number) => `${String(Math.round(value * 100))} %`;

const meta = {
  title: 'Composites/LineChart',
  component: LineChart,
  args: {
    title: 'Mujeres en activo',
    summary: '2031: 27 % de mujeres, 2 puntos más que en 2030 (provisional).',
    note: '3 arcabuceros ya no están en el registro y no cuentan.',
    categoryLabel: 'Edición',
    formatValue: percent,
    series: [{ key: 'women', label: 'Mujeres' }],
    data: shares.map((share) => ({
      id: share.year,
      label: share.year,
      provisional: share.year === '2031',
      values: { women: share.women },
    })),
  },
} satisfies Meta<typeof LineChart>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

/** Two series, told apart by their marker shapes as well as their colours. */
export const TwoSeries: Story = {
  args: {
    series: [
      { key: 'women', label: 'Mujeres' },
      { key: 'firstYear', label: 'Primer año' },
    ],
    data: shares.map((share) => ({
      id: share.year,
      label: share.year,
      provisional: share.year === '2031',
      values: { women: share.women, firstYear: share.firstYear },
    })),
  },
};
