import type { Meta, StoryObj } from '@storybook/react-vite';
import { PublicLayout } from './PublicLayout';

const meta = {
  title: 'Composites/PublicLayout',
  component: PublicLayout,
  parameters: { layout: 'fullscreen' },
  args: {
    footer: 'Versión 0.1.0',
    children: (
      <>
        <h1 className="text-xl font-semibold">Iniciar sesión</h1>
        <p className="mt-2 text-sm text-muted-foreground">Accede con tu correo y tu contraseña.</p>
      </>
    ),
  },
} satisfies Meta<typeof PublicLayout>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const WithoutFooter: Story = { args: { footer: undefined } };
