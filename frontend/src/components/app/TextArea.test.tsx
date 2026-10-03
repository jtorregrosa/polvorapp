import { zodResolver } from '@hookform/resolvers/zod';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { Button } from './Button';
import { Form, FormField } from './FormField';
import { COUNT_ANNOUNCE_DELAY_MS, TextArea } from './TextArea';
import { useAppForm } from './use-app-form';

const schema = z.object({
  reason: z.string().trim().min(1, 'validation.required').max(20, 'textArea.tooLong'),
});

type Values = z.infer<typeof schema>;

function ReasonForm({
  onSubmit,
  defaultValue = '',
}: {
  onSubmit: (values: Values) => void;
  defaultValue?: string;
}) {
  const form = useAppForm({ resolver: zodResolver(schema), defaultValues: { reason: defaultValue } });
  return (
    <Form form={form} onSubmit={onSubmit} requiredNote={false}>
      <FormField
        control={form.control}
        name="reason"
        label="Motivo"
        description="Lo leerá el jefe de disparo."
      >
        {(field) => <TextArea {...field} maxLength={20} rows={4} />}
      </FormField>
      <Button type="submit">Devolver</Button>
    </Form>
  );
}

describe('TextArea', () => {
  it('is a labelled multi-line field with its help text, limit and required state', async () => {
    await renderWithProviders(<ReasonForm onSubmit={vi.fn()} />);

    const field = screen.getByRole('textbox', { name: 'Motivo' });

    expect(field.tagName).toBe('TEXTAREA');
    expect(field).not.toHaveAttribute('maxlength');
    expect(field).toHaveAttribute('aria-required', 'true');
    expect(field).toHaveAccessibleDescription(/Lo leerá el jefe de disparo\..*Hasta 20 caracteres\./);
  });

  it('shows the characters left as the person types', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<ReasonForm onSubmit={vi.fn()} defaultValue="Hola" />);
    expect(screen.getByText('Quedan 16 caracteres')).toBeInTheDocument();

    await user.type(screen.getByRole('textbox', { name: 'Motivo' }), ' amigos y amiga');

    expect(screen.getByText('Queda 1 carácter')).toBeInTheDocument();
  });

  it('announces the count politely once typing pauses', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<ReasonForm onSubmit={vi.fn()} />);
    const status = screen.getByRole('status');
    expect(status).toHaveTextContent('');

    await user.type(screen.getByRole('textbox', { name: 'Motivo' }), 'Falta');

    await waitFor(() => expect(status).toHaveTextContent('Quedan 15 caracteres'), {
      timeout: COUNT_ANNOUNCE_DELAY_MS * 3,
    });
  });

  it('takes text over the limit, says by how much and refuses it on submit', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<ReasonForm onSubmit={onSubmit} />);
    const field = screen.getByRole('textbox', { name: 'Motivo' });

    await user.type(field, 'x'.repeat(25));
    await user.click(screen.getByRole('button', { name: 'Devolver' }));

    expect(field).toHaveValue('x'.repeat(25));
    expect(screen.getByText('Sobran 5 caracteres')).toBeInTheDocument();
    await waitFor(() => {
      expect(field).toHaveAccessibleDescription(/Tiene más caracteres de los permitidos/);
    });
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('announces only after typing pauses, and nothing without typing', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<ReasonForm onSubmit={vi.fn()} defaultValue="Hola" />);
    const status = screen.getByRole('status');

    await user.type(screen.getByRole('textbox', { name: 'Motivo' }), 'ab');
    expect(status).toHaveTextContent('');

    await waitFor(
      () => {
        expect(status).toHaveTextContent('Quedan 14 caracteres');
      },
      { timeout: COUNT_ANNOUNCE_DELAY_MS * 3 },
    );
  });

  it('counts an uncontrolled field from its initial text', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<TextArea aria-label="Notas" maxLength={10} defaultValue="Hola" />);

    expect(screen.getByText('Quedan 6 caracteres')).toBeInTheDocument();
    await user.type(screen.getByRole('textbox', { name: 'Notas' }), '!');
    expect(screen.getByText('Quedan 5 caracteres')).toBeInTheDocument();
  });

  it('keeps line breaks and submits the text', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<ReasonForm onSubmit={onSubmit} />);

    await user.type(screen.getByRole('textbox', { name: 'Motivo' }), 'Uno{Enter}Dos');
    await user.click(screen.getByRole('button', { name: 'Devolver' }));

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({ reason: 'Uno\nDos' }, expect.anything());
    });
  });

  it('shows the translated error at the field', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<ReasonForm onSubmit={vi.fn()} />);

    await user.click(screen.getByRole('button', { name: 'Devolver' }));

    const field = screen.getByRole('textbox', { name: 'Motivo' });
    await waitFor(() => expect(field).toHaveAttribute('aria-invalid', 'true'));
    expect(field).toHaveAccessibleDescription(/Este campo es obligatorio/);
  });

  it.each(['es-ES', 'ca-ES-valencia', 'en'])('has no axe violations in %s', async (language) => {
    const { container } = await renderWithProviders(
      <ReasonForm onSubmit={vi.fn()} defaultValue="Texto" />,
      language,
    );

    expect(await axeViolations(container)).toEqual([]);
  });
});
