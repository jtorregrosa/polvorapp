import { zodResolver } from '@hookform/resolvers/zod';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { Button } from './Button';
import { Form, FormField } from './FormField';
import { INCOMPLETE_TIME, TimeInput } from './TimeInput';
import { useAppForm } from './use-app-form';

const TIME = /^([01]\d|2[0-3]):[0-5]\d$/;

const schema = z.object({
  opensAt: z
    .string()
    .min(1, 'validation.required')
    .refine((value) => value !== INCOMPLETE_TIME && TIME.test(value), 'distribution:fields.time'),
  startsAt: z.string().refine((value) => value === '' || TIME.test(value), 'distribution:fields.time'),
});

type Values = z.infer<typeof schema>;

function TimesForm({
  onSubmit,
  disabled = false,
}: {
  onSubmit: (values: Values) => void;
  disabled?: boolean;
}) {
  const form = useAppForm({ resolver: zodResolver(schema), defaultValues: { opensAt: '', startsAt: '' } });
  return (
    <Form form={form} onSubmit={onSubmit}>
      <FormField control={form.control} name="opensAt" label="Hora de apertura" description="Hora local.">
        {(field) => <TimeInput {...field} disabled={disabled} />}
      </FormField>
      <FormField control={form.control} name="startsAt" label="Hora de Comparsa Sintética Norte" optional>
        {(field) => <TimeInput {...field} clearable clearSubject="Comparsa Sintética Norte" />}
      </FormField>
      <Button
        type="button"
        onClick={() => {
          form.setValue('opensAt', '09:30');
        }}
      >
        Prerrellenar
      </Button>
      <Button type="submit">Guardar</Button>
    </Form>
  );
}

/** jsdom does not drive time segments with userEvent: changes are dispatched directly. */
function typeTime(input: HTMLElement, value: string, { incomplete = false } = {}) {
  Object.defineProperty(input, 'validity', { configurable: true, value: { badInput: incomplete } });
  fireEvent.change(input, { target: { value } });
}

describe('TimeInput', () => {
  it('is a labelled native time field in minutes, with its help text and required state', async () => {
    await renderWithProviders(<TimesForm onSubmit={vi.fn()} />);

    const input = screen.getByLabelText(/Hora de apertura/);

    expect(input).toHaveAttribute('type', 'time');
    expect(input).toHaveAttribute('step', '60');
    expect(input).toHaveAttribute('autocomplete', 'off');
    expect(input).toBeRequired();
    expect(input).toHaveAccessibleDescription(/Hora local/);
  });

  it('submits the time as HH:mm and shows a translated error when empty', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<TimesForm onSubmit={onSubmit} />);

    await user.click(screen.getByRole('button', { name: 'Guardar' }));
    const input = screen.getByLabelText(/Hora de apertura/);
    expect(input).toHaveAttribute('aria-invalid', 'true');
    await waitFor(() => {
      expect(screen.getByRole('group', { name: 'Hay un problema' })).toHaveFocus();
    });
    expect(input).toHaveAccessibleDescription(/obligatorio/i);

    typeTime(input, '09:00');
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(onSubmit).toHaveBeenCalledWith({ opensAt: '09:00', startsAt: '' }, expect.anything());
  });

  it('keeps hours and minutes when the browser reports seconds', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<TimesForm onSubmit={onSubmit} />);

    typeTime(screen.getByLabelText(/Hora de apertura/), '09:15:00');
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(onSubmit).toHaveBeenCalledWith({ opensAt: '09:15', startsAt: '' }, expect.anything());
  });

  it('reports a partly typed time as incomplete, not as empty', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<TimesForm onSubmit={onSubmit} />);
    const input = screen.getByLabelText(/Hora de apertura/);

    typeTime(input, '09:00');
    typeTime(input, '', { incomplete: true });
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(onSubmit).not.toHaveBeenCalled();
    expect(input).toHaveAccessibleDescription(/hora completa/i);
  });

  it('reports a time left partly typed as incomplete when focus leaves it', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<TimesForm onSubmit={onSubmit} />);
    const input = screen.getByLabelText(/Hora de apertura/);

    await user.click(input);
    Object.defineProperty(input, 'validity', { configurable: true, value: { badInput: true } });
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(onSubmit).not.toHaveBeenCalled();
    expect(input).toHaveAccessibleDescription(/hora completa/i);
  });

  it('shows a value set by the form', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<TimesForm onSubmit={vi.fn()} />);

    await user.click(screen.getByRole('button', { name: 'Prerrellenar' }));

    expect(screen.getByLabelText(/Hora de apertura/)).toHaveValue('09:30');
  });

  it('clears an optional time with its own named button, from the keyboard, and returns focus to it', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<TimesForm onSubmit={vi.fn()} />);
    const slot = screen.getByLabelText('Hora de Comparsa Sintética Norte (opcional)');
    expect(screen.queryByRole('button', { name: /Borrar hora/ })).not.toBeInTheDocument();

    typeTime(slot, '09:00');
    const clear = screen.getByRole('button', { name: 'Borrar hora de Comparsa Sintética Norte' });
    clear.focus();
    await user.keyboard('{Enter}');

    expect(slot).toHaveValue('');
    expect(slot).toHaveFocus();
    expect(screen.queryByRole('button', { name: /Borrar hora/ })).not.toBeInTheDocument();
  });

  it('empties a partly typed time when cleared', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<TimesForm onSubmit={vi.fn()} />);
    const slot = screen.getByLabelText('Hora de Comparsa Sintética Norte (opcional)');

    typeTime(slot, '09:00');
    typeTime(slot, '', { incomplete: true });
    // The browser still shows the typed segments, which React does not know about.
    const written = vi.spyOn(slot as HTMLInputElement, 'value', 'set');
    await user.click(screen.getByRole('button', { name: 'Borrar hora de Comparsa Sintética Norte' }));

    expect(written).toHaveBeenCalledWith('');
    expect(slot).toHaveFocus();
  });

  it('reports a partly typed time when Enter submits from the field', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<TimesForm onSubmit={onSubmit} />);
    const input = screen.getByLabelText(/Hora de apertura/);

    await user.click(input);
    Object.defineProperty(input, 'validity', { configurable: true, value: { badInput: true } });
    await user.keyboard('{Enter}');

    expect(onSubmit).not.toHaveBeenCalled();
    await waitFor(() => {
      expect(input).toHaveAccessibleDescription(/hora completa/i);
    });
  });

  it.each([
    ['ca-ES-valencia', 'Comparsa Sintètica Nord', "Esborra l'hora de Comparsa Sintètica Nord"],
    ['en', 'Synthetic North', 'Clear time of Synthetic North'],
  ])('shows the clear button as a named cross inside the field in %s', async (language, subject, name) => {
    await renderWithProviders(
      <TimeInput aria-label="Hora" value="09:00" onChange={vi.fn()} clearable clearSubject={subject} />,
      language,
    );

    const clear = screen.getByRole('button', { name });
    expect(clear).toHaveTextContent('');
    expect(clear).toHaveAttribute('title', name);
    expect(clear.parentElement).toBe(screen.getByLabelText('Hora').parentElement);
  });

  it('can be disabled', async () => {
    await renderWithProviders(<TimesForm onSubmit={vi.fn()} disabled />);

    expect(screen.getByLabelText(/Hora de apertura/)).toBeDisabled();
  });

  it('has no detectable accessibility violations', async () => {
    const { container } = await renderWithProviders(<TimesForm onSubmit={vi.fn()} />);
    typeTime(screen.getByLabelText('Hora de Comparsa Sintética Norte (opcional)'), '09:30');

    expect(await axeViolations(container)).toEqual([]);
  });
});
