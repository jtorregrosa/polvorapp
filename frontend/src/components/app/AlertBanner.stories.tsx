import type { Meta, StoryObj } from '@storybook/react-vite';
import { AlertBanner, NoticeBanner } from './AlertBanner';

const meta = {
  title: 'Composites/AlertBanner',
  component: AlertBanner,
  args: {
    severity: 'info',
    title: 'Edición 2027 abierta',
    children: 'Los jefes de disparo ya pueden preparar sus pedidos.',
  },
} satisfies Meta<typeof AlertBanner>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Info: Story = {};

export const Success: Story = { args: { severity: 'success', title: 'Pedido enviado' } };

export const Warning: Story = {
  args: { severity: 'warning', title: 'Licencia a punto de caducar', children: 'Caduca en 12 días.' },
};

export const ErrorAlert: Story = {
  args: { severity: 'error', title: 'No se ha podido guardar', children: 'Inténtalo de nuevo.' },
};

/** Long Valencian copy wraps without clipping. */
export const LongValencian: Story = {
  args: {
    severity: 'warning',
    title: 'Hi ha arcabussers amb la llicència d’armes caducada o pendent de renovació',
    children:
      'Revisa la documentació abans de tancar la comanda de pólvora de la comparsa; els avisos no bloquegen l’enviament.',
  },
};

/** The outcome of an action handed over by the previous page, e.g. after a deletion. */
export const Notice: Story = {
  render: () => <NoticeBanner notice={{ id: 1, severity: 'success', text: 'Comparsa eliminada.' }} />,
};
