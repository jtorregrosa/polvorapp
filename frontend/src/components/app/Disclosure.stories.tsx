import type { Meta, StoryObj } from '@storybook/react-vite';
import { Disclosure } from './Disclosure';

const meta = {
  title: 'Composites/Disclosure',
  component: Disclosure,
  args: {
    summary: '3 comparsas sin turno',
    children: (
      <ul>
        <li>Comparsa Sintética Norte</li>
        <li>Comparsa Sintética Sur</li>
        <li>Comparsa Sintética Este</li>
      </ul>
    ),
  },
} satisfies Meta<typeof Disclosure>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Folded: Story = {};

export const Open: Story = { args: { defaultOpen: true } };
