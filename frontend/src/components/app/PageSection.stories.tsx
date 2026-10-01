import type { Meta, StoryObj } from '@storybook/react-vite';
import { Button } from './Button';
import { PageSection } from './PageSection';

const meta = {
  title: 'Composites/PageSection',
  component: PageSection,
  args: {
    title: 'Armas propias',
    description: 'Las armas que el arcabucero tiene a su nombre.',
    children: <p className="text-sm">No tiene armas propias registradas.</p>,
  },
} satisfies Meta<typeof PageSection>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const WithActions: Story = {
  args: { actions: <Button variant="secondary">Añadir arma propia</Button> },
};

export const LongValencian: Story = {
  args: {
    title: 'Trasllat a una altra comparsa de la Federació',
    description: 'L’arcabusser passa a l’altra comparsa amb les seues armes pròpies.',
    actions: <Button variant="secondary">Afig una arma pròpia</Button>,
  },
};
