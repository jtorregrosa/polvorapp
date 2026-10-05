import type { Meta, StoryObj } from '@storybook/react-vite';
import { BarChart } from './BarChart';

// Synthetic figures of five editions; the last one is in progress.
const editions = [
  { year: '2027', active: 388, reserve: 41, owned: 301, rental: 52, loan: 20, none: 15 },
  { year: '2028', active: 395, reserve: 38, owned: 305, rental: 55, loan: 21, none: 14 },
  { year: '2029', active: 402, reserve: 35, owned: 309, rental: 58, loan: 22, none: 13 },
  { year: '2030', active: 399, reserve: 44, owned: 306, rental: 60, loan: 20, none: 13 },
  { year: '2031', active: 412, reserve: 30, owned: 318, rental: 61, loan: 21, none: 12 },
];

const meta = {
  title: 'Composites/BarChart',
  component: BarChart,
  args: {
    title: 'Arcabuceros por edición',
    summary: '2031: 412 en activo, un 3 % más que en 2030 (provisional).',
    categoryLabel: 'Edición',
    layout: 'stacked',
    series: [
      { key: 'active', label: 'En activo' },
      { key: 'reserve', label: 'Reserva' },
    ],
    data: editions.map((edition) => ({
      id: edition.year,
      label: edition.year,
      provisional: edition.year === '2031',
      values: { active: edition.active, reserve: edition.reserve },
    })),
  },
} satisfies Meta<typeof BarChart>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Stacked: Story = {};

/** Weapon sources as shares of each edition: four series, each with its own pattern. */
export const Percent: Story = {
  args: {
    title: 'Procedencia del arma',
    summary: 'En 2031, el 77 % lleva arma propia.',
    layout: 'percent',
    series: [
      { key: 'owned', label: 'Propia' },
      { key: 'rental', label: 'Alquiler' },
      { key: 'loan', label: 'Préstamo' },
      { key: 'none', label: 'Sin arma' },
    ],
    data: editions.map((edition) => ({
      id: edition.year,
      label: edition.year,
      provisional: edition.year === '2031',
      values: { owned: edition.owned, rental: edition.rental, loan: edition.loan, none: edition.none },
    })),
  },
};

/** Settled editions only, side by side. */
export const Grouped: Story = {
  args: {
    layout: 'grouped',
    summary: '2030: 399 en activo, un 1 % menos que en 2029.',
    data: editions.slice(0, 4).map((edition) => ({
      id: edition.year,
      label: edition.year,
      values: { active: edition.active, reserve: edition.reserve },
    })),
  },
};
