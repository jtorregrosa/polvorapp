import type { Meta, StoryObj } from '@storybook/react-vite';
import { Users } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { EmptyState } from './EmptyState';

const meta = {
  title: 'Composites/EmptyState',
  component: EmptyState,
  args: {
    title: 'Todavía no hay arcabuceros',
    description: 'Añade el primero para preparar el pedido de la edición.',
    icon: Users,
    action: <Button>Añadir arcabucero</Button>,
  },
} satisfies Meta<typeof EmptyState>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Minimal: Story = { args: { description: undefined, icon: undefined, action: undefined } };

export const LongValencian: Story = {
  args: {
    title: 'Encara no hi ha cap arcabusser registrat en esta comparsa',
    description: 'Afig-ne el primer per a poder preparar la comanda de pólvora de l’edició.',
    action: <Button>Afig un arcabusser</Button>,
  },
};
