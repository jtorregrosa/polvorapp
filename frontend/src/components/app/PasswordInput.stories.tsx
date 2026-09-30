import type { Meta, StoryObj } from '@storybook/react-vite';
import { PasswordInput } from './PasswordInput';

const meta = {
  title: 'Composites/PasswordInput',
  component: PasswordInput,
  args: {
    'aria-label': 'Contraseña',
    autoComplete: 'current-password',
    defaultValue: 'frase-sintetica-larga',
  },
} satisfies Meta<typeof PasswordInput>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const NewPassword: Story = { args: { autoComplete: 'new-password', defaultValue: '' } };
