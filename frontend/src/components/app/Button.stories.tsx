import type { Meta, StoryObj } from '@storybook/react-vite';
import { Send, Trash2 } from 'lucide-react';
import { Button } from './Button';

const meta = {
  title: 'Composites/Button',
  component: Button,
  args: { children: 'Enviar invitación', type: 'button' },
} satisfies Meta<typeof Button>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Primary: Story = { args: { icon: Send } };

export const Secondary: Story = { args: { variant: 'secondary', children: 'Cancelar' } };

export const Destructive: Story = {
  args: { variant: 'destructive', icon: Trash2, children: 'Desactivar usuario' },
};

export const Pending: Story = { args: { pending: true, children: 'Guardando' } };

export const LongValencian: Story = { args: { children: 'Restablir la verificació en dos passos' } };
