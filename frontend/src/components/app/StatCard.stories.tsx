import type { Meta, StoryObj } from '@storybook/react-vite';
import { StatCard } from './StatCard';

const meta = {
  title: 'Composites/StatCard',
  component: StatCard,
  args: { label: 'Arcabuceros', value: '42', description: '+3 respecto a 2026', className: 'max-w-xs' },
} satisfies Meta<typeof StatCard>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Linked: Story = { args: { to: '/arquebusiers' } };

export const LongValencian: Story = {
  args: {
    label: 'Arcabussers amb la llicència caducada',
    value: '7',
    description: 'Cal revisar-los abans de tancar la comanda',
    to: '/arquebusiers',
  },
};
