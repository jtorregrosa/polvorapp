import type { Meta, StoryObj } from '@storybook/react-vite';
import { ClipboardList, House, Users } from 'lucide-react';
import { AppLayout } from './AppLayout';
import { PageHeader } from './PageHeader';
import { StatCard } from './StatCard';

const meta = {
  title: 'Composites/AppLayout',
  component: AppLayout,
  parameters: { layout: 'fullscreen' },
  args: {
    navigation: [
      { to: '/', label: 'Inicio', icon: House },
      { to: '/arquebusiers', label: 'Arcabuceros', icon: Users, count: 42 },
      { to: '/orders', label: 'Pedidos', icon: ClipboardList, count: 3 },
    ],
    sidebarFooter: <p className="text-xs text-sidebar-foreground">API 0.1.0</p>,
    children: (
      <div className="p-4 sm:p-6">
        <PageHeader title="Inicio" description="Resumen de la edición en curso" />
        <div className="grid gap-4 sm:grid-cols-3">
          <StatCard label="Arcabuceros" value="42" />
          <StatCard label="Licencias caducadas" value="3" />
          <StatCard label="Pedidos pendientes" value="1" />
        </div>
      </div>
    ),
  },
} satisfies Meta<typeof AppLayout>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

/** Long Valencian navigation labels. */
export const LongValencian: Story = {
  args: {
    navigation: [
      { to: '/', label: 'Inici', icon: House },
      { to: '/arquebusiers', label: 'Arcabussers de la comparsa', icon: Users, count: 128 },
      { to: '/orders', label: 'Comandes de pólvora pendents', icon: ClipboardList, count: 12 },
    ],
  },
};
