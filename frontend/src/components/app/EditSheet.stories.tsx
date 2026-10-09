import { zodResolver } from '@hookform/resolvers/zod';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { Trash2 } from 'lucide-react';
import { useState } from 'react';
import { z } from 'zod';
import { Button } from './Button';
import { DescriptionList } from './DescriptionList';
import { EditSheet, type EditResult } from './EditSheet';
import { FormField } from './FormField';
import { SaveNoticeProvider } from './SaveNotice';
import { SectionCard } from './SectionCard';
import { TextInput } from './TextInput';
import { useAppForm } from './use-app-form';

const schema = z.object({
  lastName: z.string().trim().min(1, 'validation.required'),
  phone: z.string(),
});
type Values = z.infer<typeof schema>;

function PersonalData({ outcome }: { outcome: EditResult }) {
  const [record, setRecord] = useState<Values>({ lastName: 'Sintética Pérez', phone: '' });
  const form = useAppForm<Values>({ resolver: zodResolver(schema), defaultValues: record });
  return (
    <SaveNoticeProvider>
      <div className="max-w-form">
        <SectionCard
          title="Datos personales"
          action={
            <EditSheet
              title="Editar datos personales"
              sectionName="datos personales"
              form={form}
              values={record}
              onSave={(values) => {
                if (outcome.status === 'saved') setRecord(values);
                return Promise.resolve(outcome);
              }}
            >
              <FormField control={form.control} name="lastName" label="Apellidos" width="name">
                {(field) => <TextInput {...field} autoComplete="off" />}
              </FormField>
              <FormField control={form.control} name="phone" label="Teléfono" width="short" optional>
                {(field) => <TextInput {...field} type="tel" autoComplete="off" />}
              </FormField>
            </EditSheet>
          }
        >
          <DescriptionList
            items={[
              { term: 'Apellidos', value: record.lastName },
              { term: 'Teléfono', value: record.phone },
            ]}
          />
        </SectionCard>
      </div>
    </SaveNoticeProvider>
  );
}

const meta = {
  title: 'Composites/EditSheet',
  component: PersonalData,
  args: { outcome: { status: 'saved' } },
} satisfies Meta<typeof PersonalData>;

export default meta;
type Story = StoryObj<typeof meta>;

/** "Edit" opens a side panel (a bottom sheet on phones); saving closes it and announces it. */
export const Default: Story = {};

/** Someone else changed the record: the panel stays open with the reason. */
export const Conflict: Story = {
  args: {
    outcome: { status: 'conflict', reason: 'Otra persona ha cambiado este registro. Revisa los datos.' },
  },
};

/**
 * Opened from a row instead of its own "Edit" button: the screen owns `open`, names the save
 * action ("Registrar entrega") and adds another action on the record at the start of the footer.
 */
export const OpenedFromARow: Story = {
  render: function Render() {
    const [open, setOpen] = useState(false);
    const form = useAppForm<Values>({
      resolver: zodResolver(schema),
      defaultValues: { lastName: '', phone: '' },
    });
    return (
      <SaveNoticeProvider>
        <Button
          variant="secondary"
          onClick={() => {
            setOpen(true);
          }}
        >
          Nº 1 · Sintética Pérez, Ana
        </Button>
        <EditSheet
          title="Entrega de Sintética Pérez, Ana"
          sectionName="entrega"
          form={form}
          values={{ lastName: '', phone: '' }}
          open={open}
          onOpenChange={setOpen}
          submitLabel="Registrar entrega"
          footerStart={
            <Button
              type="button"
              variant="quietDestructive"
              size="sm"
              icon={Trash2}
              onClick={() => {
                setOpen(false);
              }}
            >
              Quitar entrega
            </Button>
          }
          onSave={() => Promise.resolve({ status: 'saved', notice: 'Entrega registrada.' })}
        >
          <FormField control={form.control} name="lastName" label="Nº de cantimplora" width="short">
            {(field) => <TextInput {...field} autoComplete="off" />}
          </FormField>
        </EditSheet>
      </SaveNoticeProvider>
    );
  },
};
