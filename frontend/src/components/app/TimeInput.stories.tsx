import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { TimeInput, type TimeInputProps } from './TimeInput';

/** A controlled field, as forms use it. */
function Controlled({ value: initial = '', ...props }: TimeInputProps) {
  const [value, setValue] = useState(initial);
  return <TimeInput {...props} value={value} onChange={setValue} />;
}

const meta = {
  title: 'Composites/TimeInput',
  component: TimeInput,
  render: (args) => <Controlled {...args} />,
  args: { 'aria-label': 'Hora de Comparsa Sintética Norte' },
} satisfies Meta<typeof TimeInput>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Empty: Story = {};

export const Filled: Story = { args: { value: '09:30' } };

/** An optional time (e.g. a distribution slot) offers a button to empty it, because some mobile pickers cannot. */
export const Clearable: Story = {
  args: { value: '09:00', clearable: true, clearSubject: 'Comparsa Sintética Norte' },
};

export const Invalid: Story = { args: { 'aria-invalid': true, value: '07:00' } };

export const Disabled: Story = { args: { disabled: true, value: '09:00' } };
