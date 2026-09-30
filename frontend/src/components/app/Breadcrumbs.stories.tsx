import type { Meta, StoryObj } from '@storybook/react-vite';
import type { CatalogueRouteParameters } from '../../../.storybook/preview';
import { Breadcrumbs } from './Breadcrumbs';

const meta = {
  title: 'Composites/Breadcrumbs',
  component: Breadcrumbs,
} satisfies Meta<typeof Breadcrumbs>;

export default meta;
type Story = StoryObj<typeof meta>;

/** Only the start page: no route declares a breadcrumb. */
export const StartPage: Story = {};

/** Crumbs come from the `breadcrumb` handle of each matched route; the last one is the current page. */
export const Nested: Story = {
  parameters: {
    route: {
      crumbs: [
        { path: 'section', breadcrumb: 'error.title' },
        { path: 'page', breadcrumb: 'notFound.title' },
      ],
    } satisfies CatalogueRouteParameters,
  },
};
