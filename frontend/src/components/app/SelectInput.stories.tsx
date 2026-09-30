import type { Meta, StoryObj } from '@storybook/react-vite';
import { SelectInput } from './SelectInput';

const meta = {
  title: 'Composites/SelectInput',
  component: SelectInput,
  args: {
    'aria-label': 'Rol',
    options: [
      { value: 'FIRING_CHIEF', label: 'Jefe de disparo' },
      { value: 'ADMIN', label: 'Administrador' },
    ],
  },
} satisfies Meta<typeof SelectInput>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Languages: Story = {
  args: {
    'aria-label': 'Idioma de los correos',
    options: [
      { value: 'es-ES', label: 'Español', lang: 'es-ES' },
      { value: 'ca-ES-valencia', label: 'Valencià', lang: 'ca-ES-valencia' },
      { value: 'en', label: 'English', lang: 'en' },
    ],
  },
};
