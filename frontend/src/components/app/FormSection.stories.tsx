import type { Meta, StoryObj } from '@storybook/react-vite';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { FormSection } from './FormSection';

const meta = {
  title: 'Composites/FormSection',
  component: FormSection,
  args: {
    title: 'Contacto',
    description: 'Solo lo ve la comparsa',
    children: (
      <div className="flex flex-col gap-2">
        <Label htmlFor="story-phone">Teléfono</Label>
        <Input id="story-phone" type="tel" />
      </div>
    ),
  },
} satisfies Meta<typeof FormSection>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const WithoutDescription: Story = { args: { description: undefined } };

export const LongValencian: Story = {
  args: {
    title: 'Dades de contacte de la persona responsable',
    description: 'Només les veu la comparsa i s’usen per a avisos de la Federació',
  },
};
