import type { Meta, StoryObj } from '@storybook/react-vite';
import { LanguageSwitcher } from './LanguageSwitcher';

const meta = {
  title: 'Composites/LanguageSwitcher',
  component: LanguageSwitcher,
} satisfies Meta<typeof LanguageSwitcher>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
