import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { StatFilter, type StatFilterItem } from './StatFilter';

const COUNTERS: Omit<StatFilterItem, 'pressed' | 'onPressedChange'>[] = [
  { id: 'active', label: 'En activo', count: 412 },
  { id: 'reserve', label: 'En reserva', count: 38 },
  { id: 'expired', label: 'Licencia caducada', count: 17, tone: 'warning', hint: 'No pueden disparar' },
  { id: 'pending', label: 'Licencia pendiente', count: 9 },
  { id: 'none', label: 'Sin licencia', count: 21 },
];

function Counters({ initial }: { initial?: string }) {
  const [pressed, setPressed] = useState(initial);
  return (
    <StatFilter
      label="Resumen de arcabuceros"
      items={COUNTERS.map((counter) => ({
        ...counter,
        pressed: pressed === counter.id,
        onPressedChange: (on) => {
          setPressed(on ? counter.id : undefined);
        },
      }))}
    />
  );
}

const meta = {
  title: 'Composites/StatFilter',
  component: Counters,
} satisfies Meta<typeof Counters>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Pressed: Story = { args: { initial: 'expired' } };
