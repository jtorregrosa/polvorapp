import type { Meta, StoryObj } from '@storybook/react-vite';
import { Breakdown } from './Breakdown';

const meta = {
  title: 'Composites/Breakdown',
  component: Breakdown,
  args: {
    title: 'Género',
    categoryLabel: 'Género',
    columns: [{ id: 'count', label: 'Arcabuceros' }],
    rows: [
      { id: 'FEMALE', label: 'Mujeres', counts: [31] },
      { id: 'MALE', label: 'Hombres', counts: [64] },
      { id: 'UNSPECIFIED', label: 'Sin definir', counts: [5] },
    ],
    total: 100,
  },
} satisfies Meta<typeof Breakdown>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

/** The equality report: age brackets split by gender, with each row's total. */
export const ByGender: Story = {
  args: {
    title: 'Tramos de edad por género',
    categoryLabel: 'Tramo de edad',
    columns: [
      { id: 'FEMALE', label: 'Mujeres' },
      { id: 'MALE', label: 'Hombres' },
      { id: 'UNSPECIFIED', label: 'Sin definir' },
    ],
    rows: [
      { id: 'UNDER_25', label: 'Menos de 25', counts: [6, 9, 1] },
      { id: 'FROM_25_TO_34', label: '25 a 34', counts: [8, 17, 2] },
      { id: 'FROM_35_TO_44', label: '35 a 44', counts: [9, 20, 1] },
      { id: 'FROM_45', label: '45 o más', counts: [8, 18, 1] },
    ],
    showRowTotal: true,
  },
};

/** No arquebusier matches: counts without shares or bars. */
export const ZeroTotal: Story = {
  args: {
    rows: [
      { id: 'FEMALE', label: 'Mujeres', counts: [0] },
      { id: 'MALE', label: 'Hombres', counts: [0] },
      { id: 'UNSPECIFIED', label: 'Sin definir', counts: [0] },
    ],
    total: 0,
  },
};
