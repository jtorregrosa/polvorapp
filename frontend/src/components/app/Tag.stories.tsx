import type { Meta, StoryObj } from '@storybook/react-vite';
import { TAG_MAP, type TagCategory } from './tags';
import { CategoryTag } from './Tag';

const meta = {
  title: 'Composites/Tag',
  component: CategoryTag,
  args: { category: 'side', value: 'MOORISH' },
} satisfies Meta<typeof CategoryTag>;

export default meta;
type Story = StoryObj<typeof meta>;

export const MoorishSide: Story = {};

/** Every fixed value shown as a tag, grouped by category (refine-navigation-and-lists D5). */
export const AllTags: Story = {
  render: () => (
    <dl className="grid gap-4">
      {(Object.keys(TAG_MAP) as TagCategory[]).map((category) => (
        <div key={category} className="flex flex-col gap-2">
          <dt className="text-sm font-medium text-muted-foreground">{category}</dt>
          <dd className="flex flex-wrap gap-2">
            {Object.keys(TAG_MAP[category]).map((value) => (
              <CategoryTag key={value} category={category} value={value} />
            ))}
          </dd>
        </div>
      ))}
    </dl>
  ),
};

export const NotRentable: Story = { args: { category: 'yesNo', value: 'NO' } };

export const UnknownValue: Story = { args: { category: 'side', value: 'NEUTRAL' } };
