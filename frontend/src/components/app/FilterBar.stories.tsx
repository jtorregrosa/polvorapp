import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { FilterBar, NoMatches } from './FilterBar';
import { FilterSelect } from './FilterSelect';
import { SearchField } from './SearchField';

function Bar({ resultText }: { resultText: string }) {
  const [comparsa, setComparsa] = useState('');
  const [search, setSearch] = useState('');
  return (
    <FilterBar
      filters={
        <FilterSelect
          label="Comparsa"
          value={comparsa}
          onChange={setComparsa}
          options={[
            { value: '', label: 'Todas' },
            { value: 'n', label: 'Comparsa Sintética Norte' },
            { value: 's', label: 'Comparsa Sintética Sur' },
          ]}
        />
      }
      search={
        <SearchField
          label="Buscar"
          hint="Nombre, DNI/NIE o número federativo"
          value={search}
          onChange={setSearch}
        />
      }
      resultText={resultText}
    />
  );
}

const meta = {
  title: 'Composites/FilterBar',
  component: Bar,
  args: { resultText: '497 arcabuceros' },
} satisfies Meta<typeof Bar>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

/** The filters leave nothing: say so and offer to clear them. */
export const NothingMatches: Story = {
  args: { resultText: '0 arcabuceros' },
  render: (args) => (
    <div className="flex flex-col gap-section">
      <Bar {...args} />
      <NoMatches title="Ningún arcabucero coincide con los filtros" onClear={() => undefined} />
    </div>
  ),
};
