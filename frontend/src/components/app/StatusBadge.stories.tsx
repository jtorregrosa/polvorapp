import type { Meta, StoryObj } from '@storybook/react-vite';
import { STATUS_MAP, type StatusKind } from './status';
import { StatusBadge } from './StatusBadge';

const meta = {
  title: 'Composites/StatusBadge',
  component: StatusBadge,
  args: { kind: 'license', value: 'EXPIRED' },
} satisfies Meta<typeof StatusBadge>;

export default meta;
type Story = StoryObj<typeof meta>;

export const ExpiredLicense: Story = {};

/** Every domain status of the glossary, grouped by kind (design D6). */
export const AllStatuses: Story = {
  render: () => (
    <dl className="grid gap-4">
      {(Object.keys(STATUS_MAP) as StatusKind[]).map((kind) => (
        <div key={kind} className="flex flex-col gap-2">
          <dt className="text-sm font-medium text-muted-foreground">{kind}</dt>
          <dd className="flex flex-wrap gap-2">
            {Object.keys(STATUS_MAP[kind]).map((value) => (
              <StatusBadge key={value} kind={kind} value={value} />
            ))}
          </dd>
        </div>
      ))}
    </dl>
  ),
};

export const UnknownValue: Story = { args: { kind: 'order', value: 'ARCHIVED' } };
