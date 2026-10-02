import type { Meta, StoryObj } from '@storybook/react-vite';
import { ErrorSummary } from './ErrorSummary';

const meta = {
  title: 'Composites/ErrorSummary',
  component: ErrorSummary,
  args: {
    title: 'Hay un problema',
    items: [
      { text: 'DNI o NIE: Este campo es obligatorio', fieldId: 'national-id' },
      { text: 'Fecha de nacimiento: Escribe una fecha completa', fieldId: 'birth-date' },
    ],
  },
  decorators: [
    (Story) => (
      <div className="flex max-w-form flex-col gap-group">
        <Story />
        <label className="flex flex-col gap-field text-label" htmlFor="national-id">
          DNI o NIE
          <input id="national-id" className="h-control rounded-md border border-input bg-card px-3" />
        </label>
        <label className="flex flex-col gap-field text-label" htmlFor="birth-date">
          Fecha de nacimiento
          <input
            id="birth-date"
            type="date"
            className="h-control rounded-md border border-input bg-card px-3"
          />
        </label>
      </div>
    ),
  ],
} satisfies Meta<typeof ErrorSummary>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

/** An error that belongs to no field (e.g. a rejected save) is listed without a link. */
export const WithFormError: Story = {
  args: {
    items: [
      { text: 'Otra persona ha cambiado este registro. Revisa los datos.' },
      { text: 'DNI o NIE: Este campo es obligatorio', fieldId: 'national-id' },
    ],
  },
};
