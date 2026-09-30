import type { Meta, StoryObj } from '@storybook/react-vite';
import { Button } from '@/components/ui/button';
import { PageHeader } from './PageHeader';

const meta = {
  title: 'Composites/PageHeader',
  component: PageHeader,
  args: { title: 'Arcabuceros', description: '42 personas en la edición 2027' },
} satisfies Meta<typeof PageHeader>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const WithBackAndActions: Story = {
  args: {
    back: { to: '/', label: 'Inicio' },
    actions: (
      <>
        <Button variant="outline">Exportar</Button>
        <Button>Añadir arcabucero</Button>
      </>
    ),
  },
};

export const LongValencian: Story = {
  args: {
    title: 'Comanda de pólvora de la comparsa per a l’edició 2027',
    description: 'Revisa les quantitats de cada arcabusser abans d’enviar-la a la Federació',
    back: { to: '/', label: 'Totes les comandes' },
    actions: <Button>Envia la comanda</Button>,
  },
};
