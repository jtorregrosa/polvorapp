import type { Meta, StoryObj } from '@storybook/react-vite';
import { AmountTable } from './AmountTable';

const meta = {
  title: 'Composites/AmountTable',
  component: AmountTable,
  args: {
    title: 'Resumen de pago',
    rows: [
      { id: 'POWDER', label: 'Pólvora', quantity: '5 kg', unitPrice: '55,00 €', amount: '275,00 €' },
      { id: 'CAPS', label: 'Pistones', quantity: '3 cajas', unitPrice: '4,50 €', amount: '13,50 €' },
      {
        id: 'WEAPON_RENTAL',
        label: 'Alquiler de arma',
        quantity: '2 alquileres',
        unitPrice: '30,00 €',
        amount: '60,00 €',
      },
      {
        id: 'FLASK_RENTAL',
        label: 'Alquiler de cantimplora',
        quantity: '2 alquileres',
        unitPrice: '6,00 €',
        amount: '12,00 €',
      },
    ],
    total: '360,50 €',
  },
} satisfies Meta<typeof AmountTable>;

export default meta;
type Story = StoryObj<typeof meta>;

/** The billing summary of the spec's example. */
export const Default: Story = {};

/** The longest labels (Valencian): check them at a phone width, where they wrap and the page never scrolls. */
export const LongLabels: Story = {
  args: {
    title: 'Resum de pagament',
    rows: [
      { id: 'POWDER', label: 'Pólvora', quantity: '5 kg', unitPrice: '55,00 €', amount: '275,00 €' },
      { id: 'CAPS', label: 'Pistons', quantity: '3 caixes', unitPrice: '4,50 €', amount: '13,50 €' },
      {
        id: 'WEAPON_RENTAL',
        label: "Lloguer d'arma",
        quantity: '2 lloguers',
        unitPrice: '30,00 €',
        amount: '60,00 €',
      },
      {
        id: 'FLASK_RENTAL',
        label: 'Lloguer de cantimplora',
        quantity: '2 lloguers',
        unitPrice: '6,00 €',
        amount: '12,00 €',
      },
    ],
    total: '360,50 €',
  },
};

/** A price not set (an edition moved back to draft): that line has no amount and there is no total. */
export const MissingPrice: Story = {
  args: {
    rows: [
      { id: 'POWDER', label: 'Pólvora', quantity: '5 kg', unitPrice: '55,00 €', amount: '275,00 €' },
      {
        id: 'FLASK_RENTAL',
        label: 'Alquiler de cantimplora',
        quantity: '2 alquileres',
        unitPrice: null,
        amount: null,
      },
    ],
    total: null,
  },
};
