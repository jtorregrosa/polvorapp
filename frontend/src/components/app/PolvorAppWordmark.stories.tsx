import type { Meta, StoryObj } from '@storybook/react-vite';
import { PolvorAppMark } from './PolvorAppMark';
import { PolvorAppWordmark } from './PolvorAppWordmark';

const meta = {
  title: 'Composites/PolvorAppWordmark',
  component: PolvorAppWordmark,
  args: { label: 'PolvorApp' },
} satisfies Meta<typeof PolvorAppWordmark>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

/** The horizontal lockup, as in the sidebar and the public header: mark and wordmark, no gap. */
export const WithMark: Story = {
  render: (args) => (
    <span className="flex items-center text-foreground">
      <PolvorAppMark />
      <PolvorAppWordmark {...args} />
    </span>
  ),
};

/** On the night surface the letters take the sidebar text colour. */
export const OnNight: Story = {
  render: (args) => (
    <span className="flex items-center rounded-md bg-sidebar p-4 text-sidebar-foreground">
      <PolvorAppMark />
      <PolvorAppWordmark {...args} />
    </span>
  ),
};
