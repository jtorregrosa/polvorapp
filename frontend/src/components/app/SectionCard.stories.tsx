import type { Meta, StoryObj } from '@storybook/react-vite';
import { Pencil } from 'lucide-react';
import { Button } from './Button';
import { DescriptionList } from './DescriptionList';
import { SectionCard } from './SectionCard';

const meta = {
  title: 'Composites/SectionCard',
  component: SectionCard,
  args: {
    title: 'Datos personales',
    description: 'Como figuran en el documento de identidad.',
    action: (
      <Button variant="secondary" size="sm" icon={Pencil}>
        Editar
      </Button>
    ),
    children: (
      <DescriptionList
        items={[
          { term: 'Nombre', value: 'Ana' },
          { term: 'Apellidos', value: 'Sintética Pérez' },
          { term: 'DNI/NIE', value: '00000001R', mono: true },
          { term: 'Teléfono', value: '' },
        ]}
      />
    ),
  },
} satisfies Meta<typeof SectionCard>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const WithoutAction: Story = { args: { action: undefined, description: undefined } };
