import type { Meta, StoryObj } from '@storybook/react-vite';
import { DescriptionList } from './DescriptionList';
import { DetailSheet } from './DetailSheet';

function AuditEntry() {
  return (
    <DetailSheet
      title="Detalles de la entrada"
      description="Lo que se registró en esta entrada del registro de auditoría."
      trigger={{ context: 'Arcabucero modificado' }}
    >
      <DescriptionList
        items={[
          { term: 'Fecha y hora', value: '12 de julio de 2030, 18:05' },
          { term: 'Usuario', value: 'Administradora Sintética' },
          { term: 'Acción', value: 'Arcabucero modificado' },
          {
            term: 'Identificador de traza',
            value: '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01',
            mono: true,
          },
        ]}
      />
    </DetailSheet>
  );
}

const meta = {
  title: 'Composites/DetailSheet',
  component: AuditEntry,
} satisfies Meta<typeof AuditEntry>;

export default meta;
type Story = StoryObj<typeof meta>;

/** "View details" opens a read-only side panel (a bottom sheet on phones). */
export const Default: Story = {};
