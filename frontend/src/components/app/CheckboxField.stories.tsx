import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { CheckboxField } from './CheckboxField';

const meta = {
  title: 'Composites/CheckboxField',
  component: CheckboxField,
  args: {
    label: 'Recordar este dispositivo durante 30 días',
    description: 'No lo marques en ordenadores compartidos.',
    checked: false,
    onCheckedChange: () => undefined,
  },
  render: function Render(args) {
    const [checked, setChecked] = useState(args.checked);
    return <CheckboxField {...args} checked={checked} onCheckedChange={setChecked} />;
  },
} satisfies Meta<typeof CheckboxField>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Checked: Story = { args: { checked: true } };

export const WithoutDescription: Story = { args: { description: undefined } };
