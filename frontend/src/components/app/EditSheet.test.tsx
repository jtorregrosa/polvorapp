import { zodResolver } from '@hookform/resolvers/zod';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import { Input } from '@/components/ui/input';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { DescriptionList } from './DescriptionList';
import { EditSheet, type EditResult } from './EditSheet';
import { FormField } from './FormField';
import { SaveNoticeProvider } from './SaveNotice';
import { useAppForm } from './use-app-form';

// Spec: Detail pages in read mode (edit a section, cancel, invalid edit, concurrent change).
const schema = z.object({
  lastName: z.string().trim().min(1, 'validation.required'),
  phone: z.string(),
});
type Values = z.infer<typeof schema>;

function PersonalSection({ save }: { save: (values: Values) => Promise<EditResult> }) {
  const [record, setRecord] = useState<Values>({ lastName: 'Sintética', phone: '600000001' });
  const form = useAppForm<Values>({ resolver: zodResolver(schema), defaultValues: record });
  return (
    <SaveNoticeProvider>
      <section aria-label="Datos personales">
        <EditSheet
          title="Editar datos personales"
          sectionName="datos personales"
          form={form}
          values={record}
          onSave={async (values) => {
            const result = await save(values);
            if (result.status === 'saved') setRecord(values);
            if (result.status === 'conflict') form.reset({ lastName: 'Cambiada', phone: '611111111' });
            return result;
          }}
        >
          <FormField control={form.control} name="lastName" label="Apellidos">
            {(field) => <Input {...field} />}
          </FormField>
          <FormField control={form.control} name="phone" label="Teléfono" optional>
            {(field) => <Input {...field} />}
          </FormField>
        </EditSheet>
        <DescriptionList
          items={[
            { term: 'Apellidos', value: record.lastName },
            { term: 'Teléfono', value: record.phone },
          ]}
        />
      </section>
    </SaveNoticeProvider>
  );
}

const edit = () => screen.getByRole('button', { name: 'Editar datos personales' });

describe('EditSheet', () => {
  afterEach(() => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 });
  });

  it('opens the section fields in a side panel, with focus inside', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<PersonalSection save={vi.fn()} />);

    await user.click(edit());

    const panel = await screen.findByRole('dialog', { name: 'Editar datos personales' });
    expect(panel).toHaveAttribute('data-side', 'right');
    expect(panel).toContainElement(document.activeElement as HTMLElement);
    expect(within(panel).getByRole('textbox', { name: 'Apellidos' })).toHaveValue('Sintética');
  });

  it('saves, closes, shows the new values, announces it and returns focus to "Edit"', async () => {
    const user = userEvent.setup();
    const save = vi.fn().mockResolvedValue({ status: 'saved' });
    await renderWithProviders(<PersonalSection save={save} />);

    await user.click(edit());
    const panel = await screen.findByRole('dialog');
    const phone = within(panel).getByRole('textbox', { name: 'Teléfono (opcional)' });
    await user.clear(phone);
    await user.type(phone, '622222222');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(save).toHaveBeenCalledWith({ lastName: 'Sintética', phone: '622222222' });
    expect(screen.getByText('622222222')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('Cambios guardados');
    });
    expect(edit()).toHaveFocus();
  });

  it('discards the changes on Escape and sends nothing', async () => {
    const user = userEvent.setup();
    const save = vi.fn();
    await renderWithProviders(<PersonalSection save={save} />);

    await user.click(edit());
    const panel = await screen.findByRole('dialog');
    await user.type(within(panel).getByRole('textbox', { name: 'Apellidos' }), ' Cambio');
    await user.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(save).not.toHaveBeenCalled();
    expect(edit()).toHaveFocus();
    await user.click(edit());
    expect(within(await screen.findByRole('dialog')).getByRole('textbox', { name: 'Apellidos' })).toHaveValue(
      'Sintética',
    );
  });

  it('discards the changes with its Cancel button too', async () => {
    const user = userEvent.setup();
    const save = vi.fn();
    await renderWithProviders(<PersonalSection save={save} />);

    await user.click(edit());
    const panel = await screen.findByRole('dialog');
    await user.type(within(panel).getByRole('textbox', { name: 'Apellidos' }), ' Cambio');
    await user.click(within(panel).getByRole('button', { name: 'Cancelar' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(save).not.toHaveBeenCalled();
    expect(edit()).toHaveFocus();
    await user.click(edit());
    expect(within(await screen.findByRole('dialog')).getByRole('textbox', { name: 'Apellidos' })).toHaveValue(
      'Sintética',
    );
  });

  it('stays open with the error summary when a value is invalid', async () => {
    const user = userEvent.setup();
    const save = vi.fn();
    await renderWithProviders(<PersonalSection save={save} />);

    await user.click(edit());
    const panel = await screen.findByRole('dialog');
    await user.clear(within(panel).getByRole('textbox', { name: 'Apellidos' }));
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(within(panel).getByRole('group', { name: 'Hay un problema' })).toHaveFocus();
    });
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(save).not.toHaveBeenCalled();
  });

  it('stays open with the reason and the current values when someone else changed the record', async () => {
    const user = userEvent.setup();
    const save = vi
      .fn()
      .mockResolvedValue({ status: 'conflict', reason: 'Otra persona ha cambiado este registro.' });
    await renderWithProviders(<PersonalSection save={save} />);

    await user.click(edit());
    const panel = await screen.findByRole('dialog');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    const summary = await within(panel).findByRole('group', { name: 'Hay un problema' });
    expect(summary).toHaveTextContent('Otra persona ha cambiado este registro.');
    expect(within(panel).getByRole('textbox', { name: 'Apellidos' })).toHaveValue('Cambiada');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('stays open with a generic reason when saving fails unexpectedly', async () => {
    const user = userEvent.setup();
    const save = vi.fn().mockRejectedValue(new Error('network'));
    await renderWithProviders(<PersonalSection save={save} />);

    await user.click(edit());
    const panel = await screen.findByRole('dialog');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    const summary = await within(panel).findByRole('group', { name: 'Hay un problema' });
    expect(summary).toHaveTextContent('No se ha podido guardar. Inténtalo de nuevo.');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('cannot be closed while it is saving', async () => {
    const user = userEvent.setup();
    let finish: (result: EditResult) => void = () => undefined;
    const save = vi.fn(() => new Promise<EditResult>((resolve) => (finish = resolve)));
    await renderWithProviders(<PersonalSection save={save} />);

    await user.click(edit());
    const panel = await screen.findByRole('dialog');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));
    await user.keyboard('{Escape}');

    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    finish({ status: 'rejected', reason: 'Rechazado por el servidor.' });
    expect(await within(panel).findByText('Rechazado por el servidor.')).toBeInTheDocument();
  });

  it('opens as a bottom sheet on a phone', async () => {
    const user = userEvent.setup();
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
    await renderWithProviders(<PersonalSection save={vi.fn()} />);

    await user.click(edit());

    expect(await screen.findByRole('dialog')).toHaveAttribute('data-side', 'bottom');
  });

  it('has no accessibility violations when open', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<PersonalSection save={vi.fn()} />);
    await user.click(edit());
    await screen.findByRole('dialog');

    expect(await axeViolations(document.body)).toEqual([]);
  });
});
