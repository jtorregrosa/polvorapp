import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { DateInput, type DateInputProps } from './DateInput';

/** A controlled field, as forms use it. */
function Controlled({ value: initial = '', ...props }: DateInputProps) {
  const [value, setValue] = useState(initial);
  return <DateInput {...props} value={value} onChange={setValue} />;
}

const meta = {
  title: 'Composites/DateInput',
  component: DateInput,
  render: (args) => <Controlled {...args} />,
  args: {
    'aria-label': 'Fecha de expedición',
    min: '1900-01-01',
    max: '2026-10-01',
  },
} satisfies Meta<typeof DateInput>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Empty: Story = {};

export const Filled: Story = { args: { value: '2024-03-10' } };

/** An optional date offers a button to empty it, because some mobile pickers cannot. */
export const Clearable: Story = {
  args: { value: '2025-11-15', clearable: true, 'aria-label': 'Fecha del curso' },
};

export const Invalid: Story = { args: { 'aria-invalid': true, value: '2030-01-01' } };

export const Disabled: Story = { args: { disabled: true, value: '2024-03-10' } };
