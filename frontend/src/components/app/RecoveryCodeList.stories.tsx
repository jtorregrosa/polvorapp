import type { Meta, StoryObj } from '@storybook/react-vite';
import { RecoveryCodeList } from './RecoveryCodeList';

const meta = {
  title: 'Composites/RecoveryCodeList',
  component: RecoveryCodeList,
  args: {
    codes: [
      'AB1CD-EF2GH',
      'IJ3KL-MN4OP',
      'QR5ST-UV6WX',
      'YZ7AB-CD8EF',
      'GH9IJ-KL0MN',
      'OP1QR-ST2UV',
      'WX3YZ-AB4CD',
      'EF5GH-IJ6KL',
      'MN7OP-QR8ST',
      'UV9WX-YZ0AB',
    ],
  },
} satisfies Meta<typeof RecoveryCodeList>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
