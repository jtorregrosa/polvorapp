import type { Meta, StoryObj } from '@storybook/react-vite';
import { DescriptionList } from './DescriptionList';
import { SectionCard } from './SectionCard';
import { SectionGrid } from './SectionGrid';

function Detail() {
  return (
    <SectionGrid>
      <SectionCard title="Datos personales">
        <DescriptionList
          items={[
            { term: 'Nombre', value: 'Ana' },
            { term: 'Fecha de nacimiento', value: '01/05/1990' },
          ]}
        />
      </SectionCard>
      <SectionCard title="Licencia">
        <DescriptionList
          items={[
            { term: 'Tipo', value: 'AE' },
            { term: 'Caduca', value: '12/03/2027' },
          ]}
        />
      </SectionCard>
      <SectionCard title="Curso">
        <DescriptionList items={[{ term: 'Realizado', value: '' }]} />
      </SectionCard>
      <SectionCard title="Armas propias" span="full">
        <p className="text-body">Dos armas.</p>
      </SectionCard>
    </SectionGrid>
  );
}

const meta = {
  title: 'Composites/SectionGrid',
  component: Detail,
} satisfies Meta<typeof Detail>;

export default meta;
type Story = StoryObj<typeof meta>;

/** One column on phones, two from 1024 px, three from 1700 px; owned weapons take the whole row. */
export const Default: Story = {};
