import type { Meta, StoryObj } from '@storybook/react-vite';
import { UserMenu } from './UserMenu';

const meta = {
  title: 'Composites/UserMenu',
  component: UserMenu,
  args: {
    name: 'Admin Sintética',
    roleLabel: 'Administrador',
    accountHref: '/account',
    onSignOut: () => undefined,
  },
} satisfies Meta<typeof UserMenu>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const LongName: Story = {
  args: { name: 'Persona Sintètica amb un nom molt llarg', roleLabel: 'Cap de disparada' },
};
