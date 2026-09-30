import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { FilterSelect } from './FilterSelect';

const meta = {
  title: 'Composites/FilterSelect',
  component: FilterSelect,
  args: {
    label: 'Bando',
    value: '',
    options: [
      { value: '', label: 'Todos' },
      { value: 'MOORISH', label: 'Moro' },
      { value: 'CHRISTIAN', label: 'Cristiano' },
    ],
    onChange: () => undefined,
  },
  render: function Render(args) {
    const [value, setValue] = useState(args.value);
    return <FilterSelect {...args} value={value} onChange={setValue} />;
  },
} satisfies Meta<typeof FilterSelect>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Chosen: Story = { args: { value: 'CHRISTIAN' } };
