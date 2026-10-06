import type { Meta, StoryObj } from '@storybook/react-vite';
import { Lock, Trash2 } from 'lucide-react';
import { MoreActionsMenu } from './MoreActionsMenu';

const meta = {
  title: 'Composites/MoreActionsMenu',
  component: MoreActionsMenu,
  args: {
    actions: [
      { id: 'lock', label: 'Bloquear registro', icon: Lock, onSelect: () => undefined },
      {
        id: 'delete',
        label: 'Borrar arcabucero',
        icon: Trash2,
        onSelect: () => undefined,
        destructive: true,
      },
    ],
  },
} satisfies Meta<typeof MoreActionsMenu>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
