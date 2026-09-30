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
