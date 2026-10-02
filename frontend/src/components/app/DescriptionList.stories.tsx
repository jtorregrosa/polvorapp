import type { Meta, StoryObj } from '@storybook/react-vite';
import { DescriptionList } from './DescriptionList';

const meta = {
  title: 'Composites/DescriptionList',
  component: DescriptionList,
  args: {
    items: [
      { term: 'Nombre', value: 'Ana' },
      { term: 'Apellidos', value: 'Sintética Pérez' },
      { term: 'DNI/NIE', value: '00000001R', mono: true },
      { term: 'Correo electrónico', value: 'ana@example.com' },
      { term: 'Teléfono', value: '' },
      { term: 'Número federativo', value: null },
    ],
  },
} satisfies Meta<typeof DescriptionList>;

export default meta;
type Story = StoryObj<typeof meta>;

/** Empty values read "Not given". */
export const Default: Story = {};
