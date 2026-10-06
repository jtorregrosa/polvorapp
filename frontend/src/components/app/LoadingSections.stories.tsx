import type { Meta, StoryObj } from '@storybook/react-vite';
import { LoadingSections } from './LoadingSections';

const meta = {
  title: 'Composites/LoadingSections',
  component: LoadingSections,
} satisfies Meta<typeof LoadingSections>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Two: Story = {};

export const Four: Story = { args: { count: 4 } };
