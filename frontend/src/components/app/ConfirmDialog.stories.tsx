import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, fn, userEvent, within } from 'storybook/test';
import { Button } from '@/components/ui/button';
import { ConfirmDialog } from './ConfirmDialog';

const meta = {
  title: 'Composites/ConfirmDialog',
  component: ConfirmDialog,
  args: {
    title: '¿Eliminar el arcabucero?',
    description: 'Se quitará de la comparsa. Esta acción no se puede deshacer.',
    confirmLabel: 'Eliminar',
    onConfirm: fn(),
    trigger: <Button variant="destructive">Eliminar</Button>,
  },
} satisfies Meta<typeof ConfirmDialog>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Closed: Story = {};

export const Open: Story = { args: { open: true, onOpenChange: fn() } };

/** A failing action keeps the dialog open and shows the error. */
export const FailingAction: Story = {
  args: {
    open: true,
    onOpenChange: fn(),
    onConfirm: fn(() => Promise.reject(new Error('Synthetic failure'))),
  },
  play: async ({ canvasElement, args }) => {
    // The dialog is portalled to the document body.
    const body = within(canvasElement.ownerDocument.body);
    await userEvent.click(body.getByRole('button', { name: args.confirmLabel }));
    await expect(await body.findByRole('alert')).toBeInTheDocument();
    await expect(body.getByRole('alertdialog')).toBeInTheDocument();
  },
};

export const LongValencian: Story = {
  args: {
    open: true,
    onOpenChange: fn(),
    title: 'Vols anul·lar la comanda de pólvora de la comparsa per a esta edició?',
    description: 'La Federació deixarà de veure-la i hauràs de tornar a crear-la des del principi.',
    confirmLabel: 'Anul·la la comanda',
  },
};
