import type { Meta, StoryObj } from '@storybook/react-vite';
import { MoneyInput } from './MoneyInput';

const meta = {
  title: 'Composites/MoneyInput',
  component: MoneyInput,
  args: {
    'aria-label': 'Precio por kilo de pólvora',
    defaultValue: '48,00',
    className: 'max-w-field-short',
  },
} satisfies Meta<typeof MoneyInput>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Empty: Story = { args: { defaultValue: '' } };

export const Invalid: Story = { args: { 'aria-invalid': true, defaultValue: '4,555' } };

export const Disabled: Story = { args: { disabled: true } };
