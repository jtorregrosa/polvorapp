import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { FilterDate } from './FilterDate';

const meta = {
  title: 'Composites/FilterDate',
  component: FilterDate,
  args: { label: 'Desde', value: '', onChange: () => undefined },
  render: function Render(args) {
    const [value, setValue] = useState(args.value);
    return <FilterDate {...args} value={value} onChange={setValue} />;
  },
} satisfies Meta<typeof FilterDate>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Chosen: Story = { args: { value: '2030-06-15' } };
