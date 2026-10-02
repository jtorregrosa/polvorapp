import type { Meta, StoryObj } from '@storybook/react-vite';
import { IdCard } from 'lucide-react';
import { LinkCard } from './LinkCard';

const meta = {
  title: 'Composites/LinkCard',
  component: LinkCard,
  args: {
    to: '/arquebusiers',
    title: 'Arcabuceros',
    description: 'Altas, licencias, cursos y fotos de los arcabuceros.',
    icon: IdCard,
  },
  decorators: [
    (Story) => (
      <div className="max-w-sm">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof LinkCard>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const WithoutIcon: Story = { args: { icon: undefined } };
