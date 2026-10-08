import type { Meta, StoryObj } from '@storybook/react-vite';
import { PolvorAppMark } from './PolvorAppMark';

const meta = {
  title: 'Composites/PolvorAppMark',
  component: PolvorAppMark,
  args: { className: 'size-12' },
} satisfies Meta<typeof PolvorAppMark>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

/** On the night sidebar, where it usually sits. */
export const OnNight: Story = {
  render: (args) => (
    <span className="inline-flex rounded-md bg-sidebar p-4">
      <PolvorAppMark {...args} />
    </span>
  ),
};
