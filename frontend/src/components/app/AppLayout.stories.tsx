import type { Meta, StoryObj } from '@storybook/react-vite';
import { ClipboardList, House, Users } from 'lucide-react';
import { AppLayout } from './AppLayout';
import { ComparsaLogo } from './ComparsaLogo';
import { PageHeader } from './PageHeader';
import { StatCard } from './StatCard';

const meta = {
  title: 'Composites/AppLayout',
  component: AppLayout,
  parameters: { layout: 'fullscreen' },
  args: {
    navigation: [
      { id: 'home', items: [{ to: '/', label: 'Inicio', icon: House }] },
      {
        id: 'registry',
        label: 'Registro',
        items: [
          { to: '/arquebusiers', label: 'Arcabuceros', icon: Users, count: 42, countLabel: '42 con avisos' },
        ],
      },
      {
        id: 'festival',
        label: 'Fiestas',
        items: [
          { to: '/orders', label: 'Pedidos', icon: ClipboardList, count: 3, countLabel: '3 pendientes' },
        ],
      },
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

/** A synthetic emblem on transparency (ADR-0012): flat shapes only. */
const SYNTHETIC_LOGO =
  'data:image/svg+xml,' +
  encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">' +
      '<path d="M256 56a200 200 0 1 0 170 306a170 170 0 1 1 0-212A200 200 0 0 0 256 56z" fill="#1c1c1e"/>' +
      '</svg>',
  );

/** A FiringChief's comparsas under the mark: one with a dark logo, one with the placeholder and a long name. */
export const WithComparsaCards: Story = {
  args: {
    sidebarCards: [
      {
        to: '/comparsas/1',
        label: 'Comparsa Sintética Norte',
        media: <ComparsaLogo src={SYNTHETIC_LOGO} size="md" />,
      },
      {
        to: '/comparsas/2',
        label: 'Comparsa Sintètica del Sud amb un nom molt llarg',
        media: <ComparsaLogo src={null} size="md" />,
      },
    ],
  },
};

/** Long Valencian navigation labels. */
export const LongValencian: Story = {
  args: {
    navigation: [
      { id: 'home', items: [{ to: '/', label: 'Inici', icon: House }] },
      {
        id: 'registry',
        label: 'Registre',
        items: [
          {
            to: '/arquebusiers',
            label: 'Arcabussers de la comparsa',
            icon: Users,
            count: 128,
            countLabel: '128 amb avisos',
          },
        ],
      },
      {
        id: 'festival',
        label: 'Festes',
        items: [
          {
            to: '/orders',
            label: 'Comandes de pólvora pendents',
            icon: ClipboardList,
            count: 12,
            countLabel: '12 pendents',
          },
        ],
      },
    ],
  },
};
