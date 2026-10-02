import type { Meta, StoryObj } from '@storybook/react-vite';
import { KeyFacts } from './KeyFacts';

const meta = {
  title: 'Composites/KeyFacts',
  component: KeyFacts,
  args: {
    label: 'Datos clave',
    items: [
      {
        id: 'license',
        label: 'Licencia caduca',
        value: '12/03/2027',
        meter: { value: 0.62, label: 'Ha pasado el 62 % de la vigencia de la licencia' },
      },
      {
        id: 'federationId',
        label: 'Número federativo',
        value: <span className="font-mono text-id">F-0042</span>,
      },
      { id: 'nationalId', label: 'DNI/NIE', value: <span className="font-mono text-id">00000001R</span> },
      { id: 'course', label: 'Curso realizado', value: '15/11/2025' },
      { id: 'weapons', label: 'Armas propias', value: '2' },
    ],
  },
} satisfies Meta<typeof KeyFacts>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
