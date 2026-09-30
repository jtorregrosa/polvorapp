import type { Meta, StoryObj } from '@storybook/react-vite';
import { TextInput } from './TextInput';

const meta = {
  title: 'Composites/TextInput',
  component: TextInput,
  args: {
    'aria-label': 'Correo electrónico',
    type: 'email',
    autoComplete: 'email',
    placeholder: 'persona@example.test',
  },
} satisfies Meta<typeof TextInput>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Invalid: Story = { args: { 'aria-invalid': true, defaultValue: 'no-es-un-correo' } };

export const Disabled: Story = { args: { disabled: true, defaultValue: 'persona@example.test' } };
