import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { SearchField } from './SearchField';

const meta = {
  title: 'Composites/SearchField',
  component: SearchField,
  args: {
    label: 'Buscar',
    hint: 'Por nombre, DNI/NIE o ID Unión.',
    value: '',
    onChange: () => undefined,
  },
  render: function Render(args) {
    const [value, setValue] = useState(args.value);
    return <SearchField {...args} value={value} onChange={setValue} />;
  },
} satisfies Meta<typeof SearchField>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const WithTerm: Story = { args: { value: 'garcía' } };

export const WithoutHint: Story = { args: { hint: undefined } };
