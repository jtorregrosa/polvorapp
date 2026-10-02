import type { Meta, StoryObj } from '@storybook/react-vite';
import { Tabs } from './Tabs';

const meta = {
  title: 'Composites/Tabs',
  component: Tabs,
  args: {
    label: 'Secciones del arcabucero',
    tabs: [
      {
        id: 'data',
        label: 'Datos',
        content: <p className="text-body">Datos personales, licencia y curso.</p>,
      },
      { id: 'weapons', label: 'Armas propias', count: 2, content: <p className="text-body">Dos armas.</p> },
    ],
  },
} satisfies Meta<typeof Tabs>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

/** On a phone the tab list scrolls sideways instead of wrapping. */
export const ManyTabsOnAPhone: Story = {
  args: {
    tabs: ['Dades', 'Llicència', 'Curs de formació', 'Armes pròpies', 'Fotografies'].map((label, index) => ({
      id: `tab-${String(index)}`,
      label,
      content: <p className="text-body">{label}</p>,
    })),
  },
  decorators: [
    (Story) => (
      <div className="max-w-90">
        <Story />
      </div>
    ),
  ],
};
