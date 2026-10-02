import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { RadioCards, type RadioCardOption } from './RadioCards';

const GENDERS: RadioCardOption[] = [
  { value: 'FEMALE', label: 'Mujer' },
  { value: 'MALE', label: 'Hombre' },
  { value: 'OTHER', label: 'Otro' },
];

const meta = {
  title: 'Composites/RadioCards',
  component: RadioCards,
  args: { 'aria-labelledby': 'radio-cards-label', options: GENDERS, onChange: () => undefined },
  render: function Render(args) {
    const [value, setValue] = useState(args.value ?? undefined);
    return (
      <div className="flex max-w-form flex-col gap-field">
        <p id="radio-cards-label" className="text-label">
          Género
        </p>
        <RadioCards {...args} value={value} onChange={setValue} />
      </div>
    );
  },
} satisfies Meta<typeof RadioCards>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Chosen: Story = { args: { value: 'MALE' } };

/** Options with a hint each, and the fields of one answer revealed under the group. */
export const WithHintsAndReveal: Story = {
  args: {
    value: 'AE',
    options: [
      { value: 'NONE', label: 'Sin licencia' },
      {
        value: 'AE',
        label: 'AE',
        hint: 'Arcabucero de exhibición',
        reveal: <p className="text-help text-muted-foreground">Aquí van las fechas de la licencia.</p>,
      },
      { value: 'D', label: 'D', hint: 'Armas de avancarga' },
    ],
  },
};

export const Invalid: Story = { args: { 'aria-invalid': true } };

/** Long Valencian labels on a 360 px screen wrap onto more lines instead of being cut. */
export const LongLabelsOnAPhone: Story = {
  args: {
    options: [
      { value: 'ACTIVE', label: 'En actiu', hint: 'Participa en els actes de la festa d’enguany' },
      {
        value: 'RESERVE',
        label: 'En reserva',
        hint: 'No dispara enguany, però continua inscrit en la comparsa',
      },
    ],
  },
  decorators: [
    (Story) => (
      <div className="max-w-90">
        <Story />
      </div>
    ),
  ],
};
