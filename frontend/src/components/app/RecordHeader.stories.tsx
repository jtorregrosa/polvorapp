import type { Meta, StoryObj } from '@storybook/react-vite';
import { ArrowRightLeft, PauseCircle, Trash2 } from 'lucide-react';
import { Button } from './Button';
import { RecordHeader } from './RecordHeader';
import { StatusBadge } from './StatusBadge';

function Photo() {
  return (
    <div className="flex size-24 items-center justify-center rounded-lg border bg-muted font-display text-figure text-muted-foreground">
      AS
    </div>
  );
}

const meta = {
  title: 'Composites/RecordHeader',
  component: RecordHeader,
  args: {
    media: <Photo />,
    context: 'Comparsa Sintética Norte · Moros',
    name: 'Ana Sintética Pérez',
    statuses: (
      <>
        <StatusBadge kind="arquebusier" value="ACTIVE" />
        <StatusBadge kind="license" value="VALID" />
      </>
    ),
    actions: (
      <Button variant="secondary" icon={ArrowRightLeft}>
        Trasladar
      </Button>
    ),
    moreActions: [
      { id: 'status', label: 'Pasar a reserva', icon: PauseCircle, onSelect: () => undefined },
      {
        id: 'delete',
        label: 'Borrar arcabucero',
        icon: Trash2,
        onSelect: () => undefined,
        destructive: true,
      },
    ],
  },
} satisfies Meta<typeof RecordHeader>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

/** Without a photo or frequent actions: only "More actions". */
export const Minimal: Story = { args: { media: undefined, actions: undefined } };

/** A long Valencian name and context wrap on a phone. */
export const LongNameOnAPhone: Story = {
  args: {
    name: 'Maria dels Àngels Sintètica i Ficticia de la Torre',
    context: 'Comparsa Sintètica del Nord · Cristians',
  },
  decorators: [
    (Story) => (
      <div className="max-w-90">
        <Story />
      </div>
    ),
  ],
};
