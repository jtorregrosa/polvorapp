import { zodResolver } from '@hookform/resolvers/zod';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import { isIsoDate } from '@/lib/dates';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { Button } from './Button';
import { DateInput, INCOMPLETE_DATE } from './DateInput';
import { Form, FormField } from './FormField';
import { useAppForm } from './use-app-form';

const schema = z.object({
  issuedOn: z
    .string()
    .min(1, 'validation.required')
    .refine((value) => value !== INCOMPLETE_DATE && isIsoDate(value), 'registry:validation.date'),
  courseOn: z.string().refine((value) => value === '' || isIsoDate(value), 'registry:validation.date'),
});

type Values = z.infer<typeof schema>;

function DatesForm({
  onSubmit,
  disabled = false,
}: {
  onSubmit: (values: Values) => void;
  disabled?: boolean;
}) {
  const form = useAppForm({ resolver: zodResolver(schema), defaultValues: { issuedOn: '', courseOn: '' } });
  return (
    <Form form={form} onSubmit={onSubmit}>
      <FormField
        control={form.control}
        name="issuedOn"
        label="Fecha de expedición"
        description="La que figura en la licencia."
      >
        {(field) => <DateInput {...field} min="1900-01-01" max="2026-10-01" disabled={disabled} />}
      </FormField>
      <FormField control={form.control} name="courseOn" label="Fecha del curso" optional>
        {(field) => <DateInput {...field} clearable />}
      </FormField>
      <Button
        type="button"
        onClick={() => {
          form.setValue('issuedOn', '2024-03-10');
        }}
      >
        Prerrellenar
      </Button>
      <Button type="submit">Guardar</Button>
    </Form>
  );
}

/** jsdom does not drive date segments with userEvent: changes are dispatched directly. */
function typeDate(input: HTMLElement, value: string, { incomplete = false } = {}) {
  Object.defineProperty(input, 'validity', { configurable: true, value: { badInput: incomplete } });
  fireEvent.change(input, { target: { value } });
}

describe('DateInput', () => {
  it('is a labelled native date field with its bounds, help text and required state', async () => {
    await renderWithProviders(<DatesForm onSubmit={vi.fn()} />);

    const input = screen.getByLabelText(/Fecha de expedición/);

    expect(input).toHaveAttribute('type', 'date');
    expect(input).toHaveAttribute('min', '1900-01-01');
    expect(input).toHaveAttribute('max', '2026-10-01');
    expect(input).toHaveAttribute('autocomplete', 'off');
    expect(input).toBeRequired();
    expect(input).toHaveAttribute('aria-required', 'true');
    expect(input).toHaveAccessibleDescription(/La que figura en la licencia/);
  });

  it('submits the ISO date and shows a translated error when empty', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<DatesForm onSubmit={onSubmit} />);

    await user.click(screen.getByRole('button', { name: 'Guardar' }));
    const input = screen.getByLabelText(/Fecha de expedición/);
    expect(input).toHaveAttribute('aria-invalid', 'true');
    await waitFor(() => {
      expect(screen.getByRole('group', { name: 'Hay un problema' })).toHaveFocus();
    });
    expect(input).toHaveAccessibleDescription(/obligatorio/i);

    typeDate(input, '2024-03-10');
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(onSubmit).toHaveBeenCalledWith({ issuedOn: '2024-03-10', courseOn: '' }, expect.anything());
  });

  it('reports a date left partly typed as incomplete when focus leaves it', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<DatesForm onSubmit={onSubmit} />);
    const input = screen.getByLabelText(/Fecha de expedición/);

    // Chromium fires no input event while only the day and month are typed.
    await user.click(input);
    Object.defineProperty(input, 'validity', { configurable: true, value: { badInput: true } });
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(onSubmit).not.toHaveBeenCalled();
    expect(input).toHaveAccessibleDescription(/fecha completa/i);
  });

  it('reports a partly typed date as incomplete, not as empty', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<DatesForm onSubmit={onSubmit} />);
    const input = screen.getByLabelText(/Fecha de expedición/);

    // A complete date first, so the edit that leaves it half typed is a real change for React.
    typeDate(input, '2024-03-10');
    typeDate(input, '', { incomplete: true });
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(onSubmit).not.toHaveBeenCalled();
    expect(input).toHaveAccessibleDescription(/fecha completa/i);
  });

  it('shows a value set by the form, e.g. a computed expiry date', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<DatesForm onSubmit={vi.fn()} />);

    await user.click(screen.getByRole('button', { name: 'Prerrellenar' }));

    expect(screen.getByLabelText(/Fecha de expedición/)).toHaveValue('2024-03-10');
  });

  it('clears an optional date and returns focus to it', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<DatesForm onSubmit={onSubmit} />);
    const course = screen.getByLabelText('Fecha del curso (opcional)');
    expect(screen.queryByRole('button', { name: 'Borrar fecha' })).not.toBeInTheDocument();

    typeDate(course, '2025-11-15');
    await user.click(screen.getByRole('button', { name: 'Borrar fecha' }));

    expect(course).toHaveValue('');
    expect(course).toHaveFocus();
    expect(screen.queryByRole('button', { name: 'Borrar fecha' })).not.toBeInTheDocument();
  });

  it('keeps room for the clear cross inside the field, set or not, so nothing moves', async () => {
    await renderWithProviders(<DatesForm onSubmit={vi.fn()} />);
    const course = screen.getByLabelText('Fecha del curso (opcional)');
    const roomBefore = course.className;

    typeDate(course, '2025-11-15');

    const clear = screen.getByRole('button', { name: 'Borrar fecha' });
    expect(clear).toHaveTextContent('');
    expect(clear.parentElement).toBe(course.parentElement);
    expect(course.className).toBe(roomBefore);
  });

  it('empties a partly typed date when cleared', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<DatesForm onSubmit={vi.fn()} />);
    const course = screen.getByLabelText('Fecha del curso (opcional)');

    typeDate(course, '2025-11-15');
    typeDate(course, '', { incomplete: true });
    // The browser still shows the typed segments, which React does not know about.
    const written = vi.spyOn(course as HTMLInputElement, 'value', 'set');
    await user.click(screen.getByRole('button', { name: 'Borrar fecha' }));

    expect(written).toHaveBeenCalledWith('');
    expect(course).toHaveFocus();
  });

  it('can be disabled', async () => {
    await renderWithProviders(<DatesForm onSubmit={vi.fn()} disabled />);

    expect(screen.getByLabelText(/Fecha de expedición/)).toBeDisabled();
  });

  it('has no detectable accessibility violations', async () => {
    const { container } = await renderWithProviders(<DatesForm onSubmit={vi.fn()} />);
    typeDate(screen.getByLabelText('Fecha del curso (opcional)'), '2025-11-15');

    expect(await axeViolations(container)).toEqual([]);
  });
});
