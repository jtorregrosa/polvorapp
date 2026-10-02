import type { Meta, StoryObj } from '@storybook/react-vite';
import { ActionBar } from './ActionBar';
import { Button } from './Button';

const meta = {
  title: 'Composites/ActionBar',
  component: ActionBar,
  args: {
    primary: <Button type="button">Guardar cambios</Button>,
    secondary: <Button variant="secondary">Cancelar</Button>,
  },
} satisfies Meta<typeof ActionBar>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const WithStatus: Story = { args: { status: 'Hay cambios sin guardar' } };

export const PrimaryOnly: Story = { args: { secondary: undefined } };

/** Long Valencian labels on a phone wrap onto a second row; the primary action stays last. */
export const LongLabelsOnAPhone: Story = {
  args: {
    primary: <Button type="button">Registra l’arcabucer</Button>,
    secondary: <Button variant="secondary">Cancel·la i torna a la llista</Button>,
  },
  decorators: [
    (Story) => (
      <div className="max-w-90">
        <Story />
      </div>
    ),
  ],
};
