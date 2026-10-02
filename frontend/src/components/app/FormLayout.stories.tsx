import type { Meta, StoryObj } from '@storybook/react-vite';
import { ActionBar } from './ActionBar';
import { Button } from './Button';
import { FormLayout } from './FormLayout';

function Fields({ count }: { count: number }) {
  return (
    <div className="flex flex-col gap-group">
      {Array.from({ length: count }, (_, index) => (
        <label key={index} className="flex max-w-field-name flex-col gap-field text-label">
          {`Campo ${String(index + 1)}`}
          <input className="h-control rounded-md border border-input bg-card px-3 text-body" />
        </label>
      ))}
    </div>
  );
}

const meta = {
  title: 'Composites/FormLayout',
  component: FormLayout,
  args: {
    sections: [
      {
        id: 'personal',
        title: 'Datos personales',
        description: 'Como figuran en el DNI.',
        content: <Fields count={4} />,
      },
      { id: 'contact', title: 'Contacto', content: <Fields count={2} /> },
      { id: 'license', title: 'Licencia de armas', content: <Fields count={3} /> },
    ],
    actions: (
      <ActionBar
        primary={<Button type="button">Registrar arcabucero</Button>}
        secondary={<Button variant="secondary">Cancelar</Button>}
      />
    ),
  },
} satisfies Meta<typeof FormLayout>;

export default meta;
type Story = StoryObj<typeof meta>;

/** The index appears from 1280 px; the action bar stays at the bottom while the form scrolls. */
export const Default: Story = {};

/** With help for the whole form, in a column from 1700 px. */
export const WithHelp: Story = {
  args: {
    help: (
      <div className="flex flex-col gap-2">
        <p className="text-label text-foreground">¿Qué datos hacen falta?</p>
        <p>Los del documento de identidad y, si la tiene, los de la licencia de armas.</p>
      </div>
    ),
  },
};
