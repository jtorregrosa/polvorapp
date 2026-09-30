import { zodResolver } from '@hookform/resolvers/zod';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useForm } from 'react-hook-form';
import { describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { Form, FormField } from './FormField';
import { FormSection } from './FormSection';

// Zod messages are translation keys of the `ui` namespace (design D9).
const schema = z.object({
  firstName: z.string().trim().min(1, 'validation.required'),
  nickname: z.string().max(3, 'Too long').optional(),
  course: z.boolean(),
});
type Values = z.infer<typeof schema>;

function PersonForm({ onSubmit }: { onSubmit: (values: Values) => void }) {
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { firstName: '', nickname: '', course: false },
  });
  return (
    <Form form={form} onSubmit={onSubmit}>
      <FormSection title="Datos personales" description="Como figuran en el DNI">
        <FormField control={form.control} name="firstName" label="Nombre" required>
          {(field) => <Input {...field} />}
        </FormField>
        <FormField
          control={form.control}
          name="nickname"
          label="Apodo"
          description="Opcional, solo uso interno"
        >
          {(field) => <Input {...field} />}
        </FormField>
        <FormField control={form.control} name="course" label="Curso realizado" required>
          {({ value, onChange, ref, onBlur, name }) => (
            <Checkbox
              ref={ref}
              name={name}
              checked={value}
              onBlur={onBlur}
              onCheckedChange={(v) => {
                onChange(v === true);
              }}
            />
          )}
        </FormField>
      </FormSection>
      <Button type="submit">Guardar</Button>
    </Form>
  );
}

describe('FormField and FormSection', () => {
  it('labels fields, marks required ones and links help text', async () => {
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);

    const firstName = screen.getByRole('textbox', { name: /Nombre/ });
    expect(firstName).toBeRequired();
    expect(screen.getByRole('textbox', { name: 'Apodo' })).toHaveAccessibleDescription(
      'Opcional, solo uso interno',
    );
    expect(screen.getByRole('group', { name: 'Datos personales' })).toHaveAccessibleDescription(
      'Como figuran en el DNI',
    );
  });

  it('marks any kind of required control for assistive technology and explains the marker', async () => {
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);

    expect(screen.getByRole('checkbox', { name: /Curso realizado/ })).toHaveAttribute(
      'aria-required',
      'true',
    );
    expect(screen.getByText('Los campos marcados con * son obligatorios.')).toBeInTheDocument();
  });

  it('only references descriptions that exist', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    for (const element of document.querySelectorAll('[aria-describedby]')) {
      for (const id of element.getAttribute('aria-describedby')?.split(' ') ?? []) {
        expect(
          document.getElementById(id),
          `#${id} referenced by ${element.outerHTML.slice(0, 60)}`,
        ).not.toBeNull();
      }
    }
  });

  it('announces errors as alerts and shows messages that are not keys as they are', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);

    await user.type(screen.getByRole('textbox', { name: 'Apodo' }), 'abcdef');
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    const alerts = await screen.findAllByRole('alert');
    expect(alerts.map((a) => a.textContent)).toEqual(
      expect.arrayContaining(['Este campo es obligatorio', 'Too long']),
    );
  });

  it('shows a translated error linked to the invalid field and focuses it on submit', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<PersonForm onSubmit={onSubmit} />);

    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    const firstName = screen.getByRole('textbox', { name: /Nombre/ });
    expect(firstName).toHaveAttribute('aria-invalid', 'true');
    expect(firstName).toHaveAccessibleDescription(/Este campo es obligatorio/);
    expect(firstName).toHaveFocus();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('translates validation messages into the active language', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />, 'ca-ES-valencia');

    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(await screen.findByText('Este camp és obligatori')).toBeInTheDocument();
  });

  it('submits valid values', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<PersonForm onSubmit={onSubmit} />);

    await user.type(screen.getByRole('textbox', { name: /Nombre/ }), 'Ana');
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(onSubmit).toHaveBeenCalledWith(
      { firstName: 'Ana', nickname: '', course: false },
      expect.anything(),
    );
  });

  it('has no accessibility violations with errors shown', async () => {
    const user = userEvent.setup();
    const { container } = await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);

    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(await axeViolations(container)).toEqual([]);
  });

  it('keeps the spacing between sections and actions when given extra classes', async () => {
    function NarrowForm() {
      const form = useForm<Values>({ defaultValues: { firstName: '', nickname: '', course: false } });
      return (
        <Form form={form} onSubmit={vi.fn()} className="max-w-xl">
          <Button type="submit">Guardar</Button>
        </Form>
      );
    }
    await renderWithProviders(<NarrowForm />);

    const form = screen.getByRole('button', { name: 'Guardar' }).closest('form');
    expect(form).toHaveClass('flex', 'flex-col', 'gap-6', 'max-w-xl');
  });
});
