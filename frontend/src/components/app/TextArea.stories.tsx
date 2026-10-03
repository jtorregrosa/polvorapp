import type { Meta, StoryObj } from '@storybook/react-vite';
import { TextArea } from './TextArea';

const meta = {
  title: 'Composites/TextArea',
  component: TextArea,
  args: {
    'aria-label': 'Motivo de la devolución',
    maxLength: 500,
    rows: 4,
  },
} satisfies Meta<typeof TextArea>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const WithText: Story = {
  args: { defaultValue: 'Revisad la cantimplora de la segunda línea.\nGracias.' },
};

export const NearTheLimit: Story = {
  args: { maxLength: 60, defaultValue: 'Revisad la cantimplora de la segunda línea, por favor.' },
};

export const OverTheLimit: Story = {
  args: { maxLength: 20, defaultValue: 'Revisad la cantimplora, por favor.' },
};

export const Invalid: Story = { args: { 'aria-invalid': true } };

export const Disabled: Story = { args: { disabled: true, defaultValue: 'Pedido validado.' } };
