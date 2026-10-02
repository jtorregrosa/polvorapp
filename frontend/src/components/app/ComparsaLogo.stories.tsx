import type { Meta, StoryObj } from '@storybook/react-vite';
import { ComparsaLogo } from './ComparsaLogo';

/** Synthetic emblems: flat shapes on transparency, never a real comparsa's logo (ADR-0012, SEC-11). */
const svg = (body: string, width = 512, height = 512) =>
  'data:image/svg+xml,' +
  encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" width="${String(width)}" height="${String(height)}" viewBox="0 0 ${String(width)} ${String(height)}">${body}</svg>`,
  );

const BANDED_DISC = svg(
  '<circle cx="256" cy="256" r="220" fill="#2f6b4f"/><rect x="36" y="220" width="440" height="72" fill="#d9a441"/>',
);
/** Near-black on transparency: it must stay visible on the light tile in the dark theme. */
const DARK_CRESCENT = svg(
  '<path d="M256 56a200 200 0 1 0 170 306a170 170 0 1 1 0-212A200 200 0 0 0 256 56z" fill="#1c1c1e"/>',
);
const WIDE_DIAMOND = svg('<path d="M20 200 300 20 580 200 300 380z" fill="#8e2c3a"/>', 600, 400);

const meta = {
  title: 'Composites/ComparsaLogo',
  component: ComparsaLogo,
  args: { src: BANDED_DISC, size: 'lg' },
} satisfies Meta<typeof ComparsaLogo>;

export default meta;
type Story = StoryObj<typeof meta>;

export const WithLogo: Story = {};

/** Check this one in the dark theme: the tile stays light. */
export const DarkTransparentLogo: Story = { args: { src: DARK_CRESCENT } };

/** A wide logo keeps its shape inside the square tile. */
export const WideLogo: Story = { args: { src: WIDE_DIAMOND } };

export const Placeholder: Story = { args: { src: null } };

/** A logo that cannot be loaded shows the placeholder, never a broken image. */
export const LoadFailure: Story = { args: { src: '/storybook-missing-logo.png' } };

/** The three sizes: list rows, the sidebar and a record header. */
export const Sizes: Story = {
  render: (args) => (
    <div className="flex items-end gap-group">
      <ComparsaLogo {...args} size="sm" />
      <ComparsaLogo {...args} size="md" />
      <ComparsaLogo {...args} size="lg" />
    </div>
  ),
};
